using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using Nokia.Phone.HereLaunchers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
using Windows.Devices.Geolocation;//GPS

namespace MarkTheSpot
{
    public partial class MainPage : PhoneApplicationPage
    {
        private IsolatedStorageSettings settings = null;
        private string locationAllowedSetting = "locationAllowed";
    
        private string isolatedStorageFile = "MarkTheSpot.dat";
        private object readLock = new object();

        private string privacyPolicy = string.Empty;
        
        private Dictionary<Guid, GPSTag> tagsDict = new Dictionary<Guid, GPSTag>();
        private Geolocator gpsLocator;

        private Size infiniteSize = new Size(Double.PositiveInfinity, Double.PositiveInfinity);

        // Constructor
        public MainPage()
        {
            InitializeComponent();

            gpsLocator = new Geolocator();

            gpsLocator.DesiredAccuracyInMeters = 10;

            Microsoft.Phone.Maps.MapsSettings.ApplicationContext.ApplicationId = "SUPER_SECRET";
            Microsoft.Phone.Maps.MapsSettings.ApplicationContext.AuthenticationToken = "SUPER_DUPER_SECRET";

            BuildApplicationBar();

        }

        #region StateMaintanence
        /// <summary>
        /// Called when navigating away from the app
        /// </summary>
        /// <param name="e"></param>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            SaveAppState();
            base.OnNavigatedFrom(e);
        }

        /// <summary>
        /// Called when navigating to the app
        /// </summary>
        /// <param name="e"></param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            RecoverAppState();
            base.OnNavigatedTo(e);

            SystemTray.ProgressIndicator = new ProgressIndicator();
        }

        /// <summary>
        /// Recovers saved GPS locations and saved app settings
        /// </summary>
        private void RecoverAppState()
        {    
            //recover saved GPS locations
            lock (readLock)
            {
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    if (store.FileExists(isolatedStorageFile))
                    {
                        using (IsolatedStorageFileStream sr = new IsolatedStorageFileStream(isolatedStorageFile, FileMode.Open, FileAccess.Read, FileShare.Read, store))
                        {
                            byte[] cipherBuffer = new byte[sr.Length];
                            sr.Read(cipherBuffer, 0, cipherBuffer.Length);

                            byte[] plainBuffer = ProtectedData.Unprotect(cipherBuffer, null); 
                            
                            string allButtons = Encoding.UTF8.GetString(plainBuffer, 0, plainBuffer.Length);
                            string[] buttons = allButtons.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                            tagsDict.Clear();

                            DynamicPanel.Children.Clear();

                            foreach (string button in buttons)
                            {
                                string[] parts = button.Split(new char[] { GPSTag.Delimiter }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length == 4)
                                {
                                    Double lat = Double.Parse(parts[0]);
                                    Double lng = Double.Parse(parts[1]);
                                    string name = parts[2];
                                    Guid i = new Guid(parts[3]);

                                    GPSTag b = new GPSTag(i, lat, lng, name, RemoveGPSTag, FindGPSTag);

                                    tagsDict.Add(b.Id, b);
                                    DynamicPanel.Children.Add(b);
                                }
                            }

                            ResizeDynamicPanel();
                        }
                    }
                }
            }

            //app settings
            settings = IsolatedStorageSettings.ApplicationSettings;

            if (!settings.Contains(locationAllowedSetting))
            {
                RequestLocationServices();
            }
        }

        /// <summary>
        /// Serializes out the apps settings and saved GPS locations to disk
        /// </summary>
        private void SaveAppState()
        {
            settings.Save();//save the application settings

            using (var store = IsolatedStorageFile.GetUserStoreForApplication())
            {
                lock (readLock)
                {
                    //clear the file
                    if (store.FileExists(isolatedStorageFile))
                    {
                        store.DeleteFile(isolatedStorageFile);
                    }

                    try
                    {
                        using (IsolatedStorageFileStream sw = new IsolatedStorageFileStream(isolatedStorageFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, store))
                        {
                            StringBuilder sb = new StringBuilder();
                            foreach (var b in tagsDict)
                            {
                                sb.Append(b.Value.ToString());
                                sb.Append(Environment.NewLine);
                            }

                            byte[] buffer = Encoding.UTF8.GetBytes(sb.ToString());

                            byte[] cipherBuffer = ProtectedData.Protect(buffer, null);
                            sw.Write(cipherBuffer, 0, cipherBuffer.Length);
                        }
                    }
                    catch (IsolatedStorageException ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }
            }
        }
        #endregion

        /// <summary>
        /// I don't know why my XAML can't find ApplicationBar, but the code behind can...
        /// </summary>
        private void BuildApplicationBar()
        {
            ApplicationBar = new ApplicationBar();

            ApplicationBar.Mode = ApplicationBarMode.Minimized;
            ApplicationBar.Opacity = 1.0;
            ApplicationBar.IsVisible = true;
            ApplicationBar.IsMenuEnabled = true;

            ApplicationBarMenuItem locationPolicyItem = new ApplicationBarMenuItem();
            locationPolicyItem.Text = "Change Location Policy";
            ApplicationBar.MenuItems.Add(locationPolicyItem);
            locationPolicyItem.Click += new EventHandler(ShowRequestLocationServices);

            ApplicationBarMenuItem privacyPolicyItem = new ApplicationBarMenuItem();
            privacyPolicyItem.Text = "View Privacy Policy";
            ApplicationBar.MenuItems.Add(privacyPolicyItem);
            privacyPolicyItem.Click += new EventHandler(ShowPrivacyPolicy);

        }

        /// <summary>
        /// Popup a dialog displaying the privacy policy
        /// </summary>
        private void ShowPrivacyPolicy(object sender, EventArgs e)
        {
            if(string.IsNullOrWhiteSpace(privacyPolicy))
            {
                var resource = Application.GetResourceStream(new Uri("PrivacyPolicy.txt", UriKind.Relative));
                TextReader tr = new StreamReader(resource.Stream);
                privacyPolicy = tr.ReadToEnd();
            }

            MessageBox.Show(privacyPolicy);
        }

        /// <summary>
        /// Pop up request from Application bar
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ShowRequestLocationServices(object sender, EventArgs e)
        {
            RequestLocationServices();
        }

        /// <summary>
        /// Pops up a dialog window requesting the user to give the app access to location services
        /// </summary>
        /// <returns>True if user has allowed use of location services, False otherwise</returns>
        private bool RequestLocationServices()
        {
            bool result = false;

            MessageBoxResult dialogResult = System.Windows.MessageBox.Show("May this app have access to your GPS location?", "Location Services", MessageBoxButton.OKCancel);
            if (dialogResult == MessageBoxResult.OK)
            {
                result = true;
                if(settings.Contains(locationAllowedSetting))
                {
                    settings[locationAllowedSetting] = true;
                }
                else
                {
                    settings.Add(locationAllowedSetting, true);
                }
            }
            else
            {
                result = false;
                if (settings.Contains(locationAllowedSetting))
                {
                    settings[locationAllowedSetting] = false;
                }
                else
                {
                    settings.Add(locationAllowedSetting, false);
                }
            }

            return result;
        }

        /// <summary>
        /// Wrapper function for getting and if necessary setting the allowance to location services
        /// </summary>
        /// <returns></returns>
        private bool LocationServicesAllowed()
        {
            if(settings.Contains(locationAllowedSetting))
            {
                return (bool)settings[locationAllowedSetting];
            }
            else
            {
                return RequestLocationServices();
            }
        }

        /// <summary>
        /// Pops up the dialog window requesting the user to name their current GPS location
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MarkBtn_Click(object sender, RoutedEventArgs e)
        {
            NamePopUp.IsOpen = true;
            DynamicPanel.Visibility = System.Windows.Visibility.Collapsed;
        }

        /// <summary>
        /// Beg the user to let us use their GPS position
        /// </summary>
        /// <returns></returns>
        private bool BegUserForLocationServices()
        {
            //make sure we're allowed the GPS location before just getting it
            if (LocationServicesAllowed() == false)
            {
                //we're not allowed the GPS, but the user wants to mark a location.  Ask them if we may have their GPS location
                if (RequestLocationServices() == false)
                {
                    return false;//User says no.
                }
                else
                {
                    return true;
                }
            }
            else
            {
                return true;
            }
        }

        private void SetProgressIndicator(bool isVisible)
        {
            SystemTray.ProgressIndicator.IsIndeterminate = isVisible;
            SystemTray.ProgressIndicator.IsVisible = isVisible;
        }

        /// <summary>
        /// Save the user's current GPS location as a GPSTag
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void SaveMarkBtn_Click(object sender, RoutedEventArgs e)
        {
            if(string.IsNullOrEmpty(SpotName.Text))
            {
                MessageBox.Show("Please name this spot.");
                return;
            }

            SetProgressIndicator(true);

            SaveMarkBtn.IsEnabled = false;
            CancelMarkBtn.IsEnabled = false;

            if(BegUserForLocationServices() == false)
            {
                return;
            }

            SystemTray.ProgressIndicator.Text = "Acquiring GPS Location";
            Geoposition gp = await GetGPSPosition();

            SystemTray.ProgressIndicator.Text = "GPS Location Acquired";
            if(gp != null)
            { 
                try
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        GPSTag b = new GPSTag(Guid.NewGuid(), gp.Coordinate.Latitude, gp.Coordinate.Longitude, SpotName.Text, RemoveGPSTag, FindGPSTag);
                        tagsDict.Add(b.Id, b);

                        SpotName.Text = string.Empty;

                        NamePopUp.IsOpen = false;

                        DynamicPanel.Visibility = System.Windows.Visibility.Visible;

                        DynamicPanel.Children.Add(b);

                        ResizeDynamicPanel();
                    });
                }
                catch (Exception)
                {
                    Console.WriteLine("There was an error while marking this spot.  Please try again.");
                }
            }

            SystemTray.ProgressIndicator.Text = string.Empty;
            SetProgressIndicator(false);

            SaveMarkBtn.IsEnabled = true;
            CancelMarkBtn.IsEnabled = true;
        }

        /// <summary>
        /// Close the location naming dialong and don't save 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CancelMarkBtn_Click(object sender, RoutedEventArgs e)
        {
            if (NamePopUp.IsOpen)
                NamePopUp.IsOpen = false;

            DynamicPanel.Visibility = System.Windows.Visibility.Visible;
        }

        /// <summary>
        /// A button has been added or removed from our dynamic grid.  Resize the screen
        /// </summary>
        private void ResizeDynamicPanel()
        {
            DynamicPanel.Measure(infiniteSize);
        }

        /// <summary>
        /// Get the current GPS location
        /// </summary>
        /// <returns></returns>
        private async Task<Geoposition> GetGPSPosition()
        {
            if (LocationServicesAllowed())
            {
                try
                {
                    Geoposition geoposition = await gpsLocator.GetGeopositionAsync(
                        maximumAge: TimeSpan.FromMinutes(5),
                        timeout: TimeSpan.FromSeconds(10)
                        );

                    return geoposition;
                }
                catch (Exception ex)
                {
                    if ((uint)ex.HResult == 0x80004004)
                    {
                        RequestLocationServices();
                    }
                    else
                    {
                        MessageBox.Show("Sorry.  Unable to access Location services at this time.");
                    }
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Removes a GPSTag button from the dynamic grid
        /// </summary>
        /// <param name="id"></param>
        private void RemoveGPSTag(Guid id)
        {
            foreach (var child in DynamicPanel.Children.ToList())
            {
                if (child is GPSTag)
                {
                    var c = child as GPSTag;
                    if (c.Id == id)
                    {
                        tagsDict.Remove(id);
                        DynamicPanel.Children.Remove(child);
                        ResizeDynamicPanel();
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Use Nokia's HERE Maps service to navigate the user back to their previous location
        /// </summary>
        /// <param name="g"></param>
        private void FindGPSTag(GPSTag g)
        {
            if (BegUserForLocationServices() == false)
            {
                return;
            }

            try
            {
                DirectionsRouteDestinationTask routeTo = new DirectionsRouteDestinationTask();
                routeTo.Destination = new System.Device.Location.GeoCoordinate(g.TagLat, g.TagLng);
                routeTo.Mode = RouteMode.Pedestrian;
                routeTo.Show();
            }
            catch(Exception)
            {
                MessageBox.Show("There was an error creating your route.  Please try again.");
            }
        }
    }
}