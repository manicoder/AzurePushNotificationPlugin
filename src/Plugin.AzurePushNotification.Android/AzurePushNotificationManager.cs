using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Plugin.AzurePushNotification.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WindowsAzure.Messaging;

namespace Plugin.AzurePushNotification
{
    /// <summary>
    /// Implementation for Feature
    /// </summary>
    public class AzurePushNotificationManager : IAzurePushNotification
    {
        static NotificationHub Hub;
        static ICollection<string> _tags = Application.Context.GetSharedPreferences(KeyGroupName, FileCreationMode.Private).GetStringSet(TagsKey, new Collection<string>());
        public string Token { get { return Application.Context.GetSharedPreferences(KeyGroupName, FileCreationMode.Private).GetString(TokenKey, string.Empty); } }

        public bool IsRegistered { get { return Application.Context.GetSharedPreferences(KeyGroupName, FileCreationMode.Private).GetBoolean(RegisteredKey, false); } }

        public string[] Tags { get { return _tags?.ToArray(); } }

        internal static PushNotificationActionReceiver ActionReceiver = null;
        static NotificationResponse delayedNotificationResponse = null;
        internal const string KeyGroupName = "Plugin.AzurePushNotification";
        internal const string TagsKey = "TagsKey";
        internal const string TokenKey = "TokenKey";
        internal const string RegisteredKey = "RegisteredKey";
        internal const string AppVersionCodeKey = "AppVersionCodeKey";
        internal const string AppVersionNameKey = "AppVersionNameKey";
        internal const string AppVersionPackageNameKey = "AppVersionPackageNameKey";
        internal const string NotificationDeletedActionId = "Plugin.AzurePushNotification.NotificationDeletedActionId";

        static IList<NotificationUserCategory> userNotificationCategories = new List<NotificationUserCategory>();
        public static string NotificationContentTitleKey { get; set; }
        public static string NotificationContentTextKey { get; set; }
        public static string NotificationContentDataKey { get; set; }
        public static int IconResource { get; set; }
        public static Android.Net.Uri SoundUri { get; set; }
        public static Color? Color { get; set; }
        public static Type NotificationActivityType { get; set; }
        public static ActivityFlags? NotificationActivityFlags { get; set; } = ActivityFlags.ClearTop | ActivityFlags.SingleTop;
        public static string DefaultNotificationChannelId { get; set; } = "AzurePushNotificationChannel";
        public static string DefaultNotificationChannelName { get; set; } = "General";

        static Context _context;
        public static void ProcessIntent(Intent intent, bool enableDelayedResponse = true)
        {
            Bundle extras = intent?.Extras;
            if (extras != null && !extras.IsEmpty)
            {
                var parameters = new Dictionary<string, object>();
                foreach (var key in extras.KeySet())
                {
                    if (!parameters.ContainsKey(key) && extras.Get(key) != null)
                        parameters.Add(key, $"{extras.Get(key)}");
                }

                NotificationManager manager = _context.GetSystemService(Context.NotificationService) as NotificationManager;
                var notificationId = extras.GetInt(DefaultPushNotificationHandler.ActionNotificationIdKey, -1);
                if (notificationId != -1)
                {
                    var notificationTag = extras.GetString(DefaultPushNotificationHandler.ActionNotificationTagKey, string.Empty);
                    if (notificationTag == null)
                        manager.Cancel(notificationId);
                    else
                        manager.Cancel(notificationTag, notificationId);
                }


                var response = new NotificationResponse(parameters, extras.GetString(DefaultPushNotificationHandler.ActionIdentifierKey, string.Empty));

                if (_onNotificationOpened == null && enableDelayedResponse)
                    delayedNotificationResponse = response;
                else
                    _onNotificationOpened?.Invoke(CrossAzurePushNotification.Current, new AzurePushNotificationResponseEventArgs(response.Data, response.Identifier, response.Type));

                CrossAzurePushNotification.Current.NotificationHandler?.OnOpened(response);
            }
        }

        public static void Initialize(Context context, string notificationHubConnectionString, string notificationHubPath, bool resetToken, bool createDefaultNotificationChannel = true)
        {

            Hub = new NotificationHub(notificationHubPath, notificationHubConnectionString, Android.App.Application.Context);

            _context = context;

            CrossAzurePushNotification.Current.NotificationHandler = CrossAzurePushNotification.Current.NotificationHandler ?? new DefaultPushNotificationHandler();

            ThreadPool.QueueUserWorkItem(state =>
            {

                var packageName = Application.Context.PackageManager.GetPackageInfo(Application.Context.PackageName, PackageInfoFlags.MetaData).PackageName;
                var versionCode = Application.Context.PackageManager.GetPackageInfo(Application.Context.PackageName, PackageInfoFlags.MetaData).VersionCode;
                var versionName = Application.Context.PackageManager.GetPackageInfo(Application.Context.PackageName, PackageInfoFlags.MetaData).VersionName;
                var prefs = Android.App.Application.Context.GetSharedPreferences(AzurePushNotificationManager.KeyGroupName, FileCreationMode.Private);

                try
                {

                    var storedVersionName = prefs.GetString(AzurePushNotificationManager.AppVersionNameKey, string.Empty);
                    var storedVersionCode = prefs.GetString(AzurePushNotificationManager.AppVersionCodeKey, string.Empty);
                    var storedPackageName = prefs.GetString(AzurePushNotificationManager.AppVersionPackageNameKey, string.Empty);


                    if (resetToken || (!string.IsNullOrEmpty(storedPackageName) && (!storedPackageName.Equals(packageName, StringComparison.CurrentCultureIgnoreCase) || !storedVersionName.Equals(versionName, StringComparison.CurrentCultureIgnoreCase) || !storedVersionCode.Equals($"{versionCode}", StringComparison.CurrentCultureIgnoreCase))))
                    {
                        CleanUp();

                    }

                }
                catch (Exception ex)
                {
                    _onNotificationError?.Invoke(CrossAzurePushNotification.Current, new AzurePushNotificationErrorEventArgs(ex.ToString()));
                }
                finally
                {
                    var editor = prefs.Edit();
                    editor.PutString(AzurePushNotificationManager.AppVersionNameKey, $"{versionName}");
                    editor.PutString(AzurePushNotificationManager.AppVersionCodeKey, $"{versionCode}");
                    editor.PutString(AzurePushNotificationManager.AppVersionPackageNameKey, $"{packageName}");
                    editor.Commit();
                }


                var token = Firebase.Iid.FirebaseInstanceId.Instance.Token;
                if (!string.IsNullOrEmpty(token))
                {

                    SaveToken(token);
                }


            });

            if (Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O && createDefaultNotificationChannel)
            {
                // Create channel to show notifications.
                string channelId = DefaultNotificationChannelId;
                string channelName = DefaultNotificationChannelName;
                NotificationManager notificationManager = (NotificationManager)context.GetSystemService(Context.NotificationService);

                notificationManager.CreateNotificationChannel(new NotificationChannel(channelId,
                    channelName, NotificationImportance.Default));
            }


            System.Diagnostics.Debug.WriteLine(CrossAzurePushNotification.Current.Token);
        }
        public static void Initialize(Context context, string notificationHubConnectionString, string notificationHubPath, NotificationUserCategory[] notificationCategories, bool resetToken, bool createDefaultNotificationChannel = true)
        {

            Initialize(context, notificationHubConnectionString, notificationHubPath, resetToken, createDefaultNotificationChannel);
            RegisterUserNotificationCategories(notificationCategories);

        }
        public static void Reset()
        {
            try
            {
                ThreadPool.QueueUserWorkItem(state =>
                {
                    CleanUp();
                });
            }
            catch (Exception ex)
            {
                _onNotificationError?.Invoke(CrossAzurePushNotification.Current, new AzurePushNotificationErrorEventArgs(ex.ToString()));
            }


        }

        static void CleanUp()
        {
            Firebase.Iid.FirebaseInstanceId.Instance.DeleteInstanceId();
            SaveToken(string.Empty);
        }


        public static void Initialize(Context context, string notificationHubConnectionString, string notificationHubPath, IPushNotificationHandler pushNotificationHandler, bool resetToken, bool createDefaultNotificationChannel = true)
        {
            CrossAzurePushNotification.Current.NotificationHandler = pushNotificationHandler;
            Initialize(context,notificationHubConnectionString,notificationHubPath, resetToken, createDefaultNotificationChannel);
        }

        public static void ClearUserNotificationCategories()
        {
            userNotificationCategories.Clear();
        }

      
        static AzurePushNotificationDataEventHandler _onNotificationReceived;
        public event AzurePushNotificationDataEventHandler OnNotificationReceived
        {
            add
            {
                _onNotificationReceived += value;
            }
            remove
            {
                _onNotificationReceived -= value;
            }
        }

        static AzurePushNotificationDataEventHandler _onNotificationDeleted;
        public event AzurePushNotificationDataEventHandler OnNotificationDeleted
        {
            add
            {
                _onNotificationDeleted += value;
            }
            remove
            {
                _onNotificationDeleted -= value;
            }
        }

        static AzurePushNotificationResponseEventHandler _onNotificationOpened;
        public event AzurePushNotificationResponseEventHandler OnNotificationOpened
        {
            add
            {
                _onNotificationOpened += value;
                if (delayedNotificationResponse != null && _onNotificationOpened == null)
                {
                    var tmpParams = delayedNotificationResponse;
                    _onNotificationOpened?.Invoke(CrossAzurePushNotification.Current, new AzurePushNotificationResponseEventArgs(tmpParams.Data, tmpParams.Identifier, tmpParams.Type));
                    delayedNotificationResponse = null;
                }
            }
            remove
            {
                _onNotificationOpened -= value;
            }
        }

        static AzurePushNotificationTokenEventHandler _onTokenRefresh;
        public event AzurePushNotificationTokenEventHandler OnTokenRefresh
        {
            add
            {
                _onTokenRefresh += value;
            }
            remove
            {
                _onTokenRefresh -= value;
            }
        }

        static AzurePushNotificationErrorEventHandler _onNotificationError;
        public event AzurePushNotificationErrorEventHandler OnNotificationError
        {
            add
            {
                _onNotificationError += value;
            }
            remove
            {
                _onNotificationError -= value;
            }
        }



        public IPushNotificationHandler NotificationHandler { get; set; }

        public NotificationUserCategory[] GetUserNotificationCategories()
        {
            return userNotificationCategories?.ToArray();
        }
        public static void RegisterUserNotificationCategories(NotificationUserCategory[] notificationCategories)
        {
            if (notificationCategories != null && notificationCategories.Length > 0)
            {
                ClearUserNotificationCategories();

                foreach (var userCat in notificationCategories)
                {
                    userNotificationCategories.Add(userCat);
                }

            }
            else
            {
                ClearUserNotificationCategories();
            }
        }

        public async Task RegisterAsync(string[] tags)
        {
            if (Hub != null)
            {
                _tags = tags;
                await Task.Run(() =>
                {
                    
                    if (!string.IsNullOrEmpty(Token))
                    {
                        try
                        {
                            Hub.UnregisterAll(Token);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"AzurePushNotification - Unregister- Error - {ex.Message}");

                            _onNotificationError?.Invoke(CrossAzurePushNotification.Current, new AzurePushNotificationErrorEventArgs(ex.Message));
                        }

                        try
                        {
                            Registration hubRegistration = null;
                     

                            if(tags !=null && tags.Length > 0)
                            {
                                hubRegistration = Hub.Register(Token,tags);
                            }
                            else
                            {
                                hubRegistration = Hub.Register(Token);
                            }
                          

                            var editor = Application.Context.GetSharedPreferences(KeyGroupName, FileCreationMode.Private).Edit();
                            editor.PutBoolean(RegisteredKey, true);
                            editor.PutStringSet(TagsKey, _tags);
                            editor.Commit();

                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"AzurePushNotification - Register - Error - {ex.Message}");
                            _onNotificationError?.Invoke(CrossAzurePushNotification.Current, new AzurePushNotificationErrorEventArgs(ex.Message));
                        }
                    }


                });
            }
        }


        public async Task UnregisterAsync()
        {
            await Task.Run(() =>
            {
                if (Hub != null && !string.IsNullOrEmpty(Token))
                {
                    try
                    {
                        Hub.UnregisterAll(Token);
                        _tags = new Collection<string>();
                        var editor = Application.Context.GetSharedPreferences(KeyGroupName, FileCreationMode.Private).Edit();
                        editor.PutBoolean(RegisteredKey, false);
                        editor.PutStringSet(TagsKey, _tags);
                        editor.Commit();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"AzurePushNotification - Error - {ex.Message}");

                        _onNotificationError?.Invoke(CrossAzurePushNotification.Current, new AzurePushNotificationErrorEventArgs(ex.Message));
                    }
                }
            });
        }

        #region internal methods
        //Raises event for push notification token refresh
        internal static async void RegisterToken(string token)
        {
            _onTokenRefresh?.Invoke(CrossAzurePushNotification.Current, new AzurePushNotificationTokenEventArgs(token));
            await CrossAzurePushNotification.Current.RegisterAsync(_tags?.ToArray());
        }
        internal static void RegisterData(IDictionary<string, object> data)
        {
            _onNotificationReceived?.Invoke(CrossAzurePushNotification.Current, new AzurePushNotificationDataEventArgs(data));
        }
        internal static void RegisterDelete(IDictionary<string, object> data)
        {
            _onNotificationDeleted?.Invoke(CrossAzurePushNotification.Current, new AzurePushNotificationDataEventArgs(data));
        }
        internal static void SaveToken(string token)
        {
            var editor = Application.Context.GetSharedPreferences(KeyGroupName, FileCreationMode.Private).Edit();
            editor.PutString(TokenKey, token);
            editor.Commit();
        }

        #endregion
    }
}