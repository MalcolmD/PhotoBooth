using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;

using Windows.ApplicationModel;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.System.Display;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

using Lumia.Imaging;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace PhotoBooth
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        MediaCapture mediaCapture;
        bool isPreviewActive;

        DeviceWatcher deviceWatcher;
        Dictionary<string, DeviceInformation> devices;
        string selectedDeviceId = string.Empty;

        int lastImageIndex;
        int maxFrameCount;
        int frameDuration;
        SoftwareBitmap[] bitmapFrames;

        DisplayRequest displayRequest;

        public MainPage()
        {
            this.InitializeComponent();

            this.devices = new Dictionary<string, DeviceInformation>();

            this.deviceWatcher = DeviceInformation.CreateWatcher(DeviceClass.VideoCapture);
            this.deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
            this.deviceWatcher.Added += DeviceWatcher_Added;
            this.deviceWatcher.Removed += DeviceWatcher_Removed;
            this.deviceWatcher.Updated += DeviceWatcher_Updated;

            this.displayRequest = new DisplayRequest();

            this.buttonPlayPause.Click += ButtonPlayPause_Click;
            this.buttonSnap.Click += ButtonSnap_Click;
            this.buttonSave.Click += ButtonSave_Click;
            this.buttonPlayKiosk.Click += ButtonPlayKiosk_Click;

            // set default max frame count
            this.maxFrameCount = 5;
            // init last image index to empty
            this.lastImageIndex = -1;
            // time for each frame in ms
            this.frameDuration = 50;


            // initialize collection for captured image frames
            this.bitmapFrames = new SoftwareBitmap[this.maxFrameCount];

            // create image xaml elements to show capture frames
            for (int i = 0; i < this.maxFrameCount; ++i)
                this.stackPanelImages.Children.Add(new Image { Width = 150 });

        }

        async Task StartPreviewAsync()
        {
            if (this.isPreviewActive)
                return;

            this.mediaCapture = new MediaCapture();

            try
            {
                MediaCaptureInitializationSettings initSettings = new MediaCaptureInitializationSettings();
                initSettings.VideoDeviceId = this.selectedDeviceId;

                await this.mediaCapture.InitializeAsync(initSettings);

                // request the system keeps the display active while preview is in-use
                this.displayRequest.RequestActive();

                // set preview element capture source
                this.capturePreview.Source = this.mediaCapture;

                // begin the capture preview
                await this.mediaCapture.StartPreviewAsync();

                // initialization successful
                this.isPreviewActive = true;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e.ToString());

                // if UnauthorizedAccessException -- show error that access was not granted...

                // if System.IO.FileLoadException -- capture preview could not be started...
            }

            return;
        }

        async Task StopPreviewAsync()
        {
            if (this.mediaCapture != null)
            {
                if (this.isPreviewActive)
                {
                    await mediaCapture.StopPreviewAsync();
                }

                // ensure camera cleanup is run on the main UI thread (according to MSDN...)
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, CleanupCamera);
            }

            this.isPreviewActive = false;
        }

        void CleanupCamera()
        {
            this.capturePreview.Source = null;

            this.displayRequest.RequestRelease();

            // release media capture instance
            this.mediaCapture.Dispose();
            this.mediaCapture = null;
        }

        async void ButtonPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (this.symbolIconPlayPause.Symbol == Symbol.Play && !this.isPreviewActive)
            {
                this.symbolIconPlayPause.Symbol = Symbol.Pause;
                await StartPreviewAsync();
            }
            else if (this.symbolIconPlayPause.Symbol == Symbol.Pause && this.isPreviewActive)
            {
                this.symbolIconPlayPause.Symbol = Symbol.Play;
                await StopPreviewAsync();
            }
        }

        async void ButtonSnap_Click(object sender, RoutedEventArgs e)
        {
            if (this.mediaCapture == null || !this.isPreviewActive)
                return;

            // get media stream properties from the capture device
            VideoEncodingProperties previewProperties = this.mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;

            // create a single preview frame using the specified format
            VideoFrame videoFrameType = new VideoFrame(BitmapPixelFormat.Bgra8, (int)previewProperties.Width, (int)previewProperties.Height);

            VideoFrame previewFrame = await this.mediaCapture.GetPreviewFrameAsync(videoFrameType);

            SoftwareBitmap previewBitmap = previewFrame.SoftwareBitmap;

            previewFrame.Dispose();
            previewFrame = null;

            if (previewBitmap != null)
            {
                int currImageIndex = (this.lastImageIndex + 1) % this.maxFrameCount;

                // check if previously captured frame should be released
                SoftwareBitmap existingBitmap = this.bitmapFrames[currImageIndex];
                if (existingBitmap != null)
                {
                    existingBitmap.Dispose();
                }

                // set the current captured bitmap frame
                this.bitmapFrames[currImageIndex] = previewBitmap;

                // create image source, needed to assign to xaml Image element
                SoftwareBitmapSource imageSource = new SoftwareBitmapSource();
                await imageSource.SetBitmapAsync(previewBitmap);

                // check if current xaml Image has previous image source associated
                Image currImage = (Image)this.stackPanelImages.Children[currImageIndex];
                if (currImage.Source != null)
                {
                    SoftwareBitmapSource releaseImageSource = (SoftwareBitmapSource)currImage.Source;
                    releaseImageSource.Dispose();
                    currImage.Source = null;
                }

                // set current Image element bitmap source
                currImage.Source = imageSource;

                // update the last set image index
                this.lastImageIndex = currImageIndex;
            }
        }

        void ButtonSave_Click(object sender, RoutedEventArgs e)
        {
            MakeGif();
        }

        async void ButtonPlayKiosk_Click(object sender, RoutedEventArgs e)
        {

            if (this.mediaCapture == null || !this.isPreviewActive)
                return;

            // get media stream properties from the capture device
            VideoEncodingProperties previewProperties = this.mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;

            for (int i = 0; i < maxFrameCount; ++i)
            {
                // create a single preview frame using the specified format
                VideoFrame videoFrameType = new VideoFrame(BitmapPixelFormat.Bgra8, (int)previewProperties.Width, (int)previewProperties.Height);

                VideoFrame previewFrame = await this.mediaCapture.GetPreviewFrameAsync(videoFrameType);

                SoftwareBitmap previewBitmap = previewFrame.SoftwareBitmap;

                previewFrame.Dispose();
                previewFrame = null;

                if (previewBitmap != null)
                {
                    int currImageIndex = (this.lastImageIndex + 1) % this.maxFrameCount;

                    // check if previously captured frame should be released
                    SoftwareBitmap existingBitmap = this.bitmapFrames[currImageIndex];
                    if (existingBitmap != null)
                    {
                        existingBitmap.Dispose();
                    }

                    // set the current captured bitmap frame
                    this.bitmapFrames[currImageIndex] = previewBitmap;

                    // create image source, needed to assign to xaml Image element
                    SoftwareBitmapSource imageSource = new SoftwareBitmapSource();
                    await imageSource.SetBitmapAsync(previewBitmap);

                    // check if current xaml Image has previous image source associated
                    Image currImage = (Image)this.stackPanelImages.Children[currImageIndex];
                    if (currImage.Source != null)
                    {
                        SoftwareBitmapSource releaseImageSource = (SoftwareBitmapSource)currImage.Source;
                        releaseImageSource.Dispose();
                        currImage.Source = null;
                    }

                    // set current Image element bitmap source
                    currImage.Source = imageSource;

                    // update the last set image index
                    this.lastImageIndex = currImageIndex;
                }

                await WaitMethod(this.frameDuration);
            }
        }

        async void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object args)
        {
            await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, RefreshDeviceMenu);
        }

        void RefreshDeviceMenu()
        {
            this.menuFlyoutDevices.Items.Clear();

            foreach (DeviceInformation device in this.devices.Values)
            {
                ToggleMenuFlyoutItem menuItem = new ToggleMenuFlyoutItem()
                {
                    Text = device.Name,
                    IsChecked = device.Id == this.selectedDeviceId,
                    Tag = device
                };
                menuItem.Click += MenuItem_Click; ;

                this.menuFlyoutDevices.Items.Add(menuItem);
            }
        }

        async void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            ToggleMenuFlyoutItem menuItem = (ToggleMenuFlyoutItem)sender;
            DeviceInformation device = (DeviceInformation)menuItem.Tag;
            this.selectedDeviceId = device.Id;

            await StopPreviewAsync();
            await StartPreviewAsync();
        }

        void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
        {
            this.devices.Add(args.Id, args);

            if (args.IsDefault)
                this.selectedDeviceId = args.Id;
        }

        void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            this.devices.Remove(args.Id);

            if (this.selectedDeviceId == args.Id)
                this.selectedDeviceId = string.Empty;
        }

        void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            DeviceInformation deviceInfo;
            if (this.devices.TryGetValue(args.Id, out deviceInfo))
            {
                deviceInfo.Update(args);
            }
        }


        /*async*/
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            this.deviceWatcher.Start();

            //await StartPreviewAsync();
        }

        /*async*/
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            //await StopPreviewAsync();
        }


        async void MakeGif()
        {
            List<IImageProvider> imageSourceList = new List<IImageProvider>();

            // add image sources to define sequence of GIF frames
            for (int i = 0; i < this.bitmapFrames.Length; ++i)
            {
                SoftwareBitmap softwareBitmap = this.bitmapFrames[i];
                if (softwareBitmap != null)
                {
                    SoftwareBitmapImageSource imageSource = new SoftwareBitmapImageSource(softwareBitmap);
                    imageSourceList.Add(imageSource);
                }
            }

            // use lumia imaging SDK component to create animated GIF image
            GifRenderer gifRenderer = new GifRenderer(imageSourceList);
            gifRenderer.Duration = 100; // time for each frame in ms
            gifRenderer.NumberOfAnimationLoops = 200; // loop continuosly
            gifRenderer.ApplyDithering = false;

            Windows.Storage.Streams.IBuffer gifBuffer = await gifRenderer.RenderAsync();


            // show animated gif in xaml preview area
            BitmapImage animBitmap = new BitmapImage();
            await animBitmap.SetSourceAsync(gifBuffer.AsStream().AsRandomAccessStream());
            // set preview animated gif
            this.imageAnimPreview.Source = animBitmap;

            bool saveImage = false;
            if (saveImage)
            {
                // write animated gif image to file
                string timeString = DateTime.Now.ToString("yyyyMMdd-HHmm_ss");
                string filename = $"PhotoBooth_{timeString}.gif";
                Windows.Storage.StorageFile storageFile = await Windows.Storage.KnownFolders.SavedPictures.CreateFileAsync(filename, Windows.Storage.CreationCollisionOption.ReplaceExisting);
                using (var stream = await storageFile.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
                {
                    await stream.WriteAsync(gifBuffer);
                }
            }
        }


        // async helpers
        async Task WaitMethod(int ms)
        {
            await Task.Delay(ms);
        }

    }
}
