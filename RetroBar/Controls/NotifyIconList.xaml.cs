using GongSolutions.Wpf.DragDrop;
using ManagedShell.WindowsTray;
using RetroBar.Extensions;
using RetroBar.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Tray = ManagedShell.WindowsTray;

namespace RetroBar.Controls
{
    /// <summary>
    /// Interaction logic for NotifyIconList.xaml
    /// </summary>
    public partial class NotifyIconList : UserControl
    {
        private bool _isLoaded;
        private ObservableCollection<Tray.NotifyIcon> promotedIcons = new ObservableCollection<Tray.NotifyIcon>();
        private NotifyIconDropHandler dropHandler;
        private ListCollectionView collectionView;
        private bool gapUpdatePending;

        public static DependencyProperty NotificationAreaProperty = DependencyProperty.Register(nameof(NotificationArea), typeof(NotificationArea), typeof(NotifyIconList), new PropertyMetadata(NotificationAreaChangedCallback));

        public NotificationArea NotificationArea
        {
            get { return (NotificationArea)GetValue(NotificationAreaProperty); }
            set { SetValue(NotificationAreaProperty, value); }
        }

        public NotifyIconList()
        {
            InitializeComponent();
        }

        private bool IsCollapsed()
        {
            return Settings.Instance.CollapseNotifyIcons && NotifyIconToggleButton.IsChecked != true;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.CollapseNotifyIcons))
            {
                if (Settings.Instance.CollapseNotifyIcons)
                {
                    SetToggleVisibility();
                }
                else
                {
                    NotifyIconToggleButton.IsChecked = false;
                    NotifyIconToggleButton.Visibility = Visibility.Collapsed;
                }
                collectionView?.Refresh();
            }
            else if (e.PropertyName == nameof(Settings.InvertIconsMode) || e.PropertyName == nameof(Settings.InvertNotifyIcons) || e.PropertyName == nameof(Settings.NotifyIconOrder))
            {
                // Reload icons
                collectionView?.Refresh();
            }
            else if (e.PropertyName == nameof(Settings.Theme))
            {
                // The theme decides how much space separates the system icons
                scheduleSystemIconGapUpdate();
            }
        }

        private void SetNotificationAreaCollections()
        {
            if (!_isLoaded && NotificationArea != null)
            {
                NotificationArea.NotificationBalloonShown += NotificationArea_NotificationBalloonShown;
                NotificationArea.UnpinnedIcons.CollectionChanged += UnpinnedIcons_CollectionChanged;
                NotificationArea.UnpinnedIcons.Filter = UnpinnedNotifyIcons_Filter;
                Settings.Instance.PropertyChanged += Settings_PropertyChanged;

                collectionView = new ListCollectionView(NotificationArea.TrayIcons);
                collectionView.CustomSort = new NotifyIconComparer(this);
                collectionView.Filter = NotifyIcons_Filter;
                var collectionViewShaping = collectionView as ICollectionViewLiveShaping;
                collectionViewShaping.IsLiveFiltering = true;
                collectionViewShaping.LiveFilteringProperties.Add("IsHidden");
                collectionViewShaping.LiveFilteringProperties.Add("IsPinned");
                NotifyIcons.ItemsSource = collectionView;
                ((INotifyCollectionChanged)collectionView).CollectionChanged += CollectionView_CollectionChanged;
                scheduleSystemIconGapUpdate();

                if (Settings.Instance.CollapseNotifyIcons)
                {
                    SetToggleVisibility();
                }

                _isLoaded = true;
            }
        }

        private static void NotificationAreaChangedCallback(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is NotifyIconList notifyIconList && e.OldValue == null && e.NewValue != null)
            {
                notifyIconList.SetNotificationAreaCollections();
            }
        }

        private bool NotifyIcons_Filter(object icon)
        {
            if (icon is Tray.NotifyIcon notifyIcon)
            {
                return (!IsCollapsed() || notifyIcon.IsPinned)
                    && !notifyIcon.IsHidden
                    && notifyIcon.GetBehavior() != NotifyIconBehavior.Remove;
            }
            return false;
        }

        private bool UnpinnedNotifyIcons_Filter(object obj)
        {
            // This filter is used when we check if the toggle should hide
            if (obj is Tray.NotifyIcon notifyIcon)
            {
                return !notifyIcon.IsPinned && !notifyIcon.IsHidden && notifyIcon.GetBehavior() != NotifyIconBehavior.Remove;
            }

            return true;
        }

        private void NotificationArea_NotificationBalloonShown(object sender, NotificationBalloonEventArgs e)
        {
            // This is used to promote unpinned icons to show when the tray is collapsed.

            if (NotificationArea == null)
            {
                return;
            }

            Tray.NotifyIcon notifyIcon = e.Balloon.NotifyIcon;

            if (NotificationArea.PinnedIcons.Contains(notifyIcon))
            {
                // Do not promote pinned icons (they're already there!)
                return;
            }

            if (notifyIcon.GetBehavior() != NotifyIconBehavior.HideWhenInactive)
            {
                // Do not promote icons that are always hidden
                return;
            }

            if (promotedIcons.Contains(notifyIcon))
            {
                // Do not duplicate promoted icons
                return;
            }

            notifyIcon.IsPinned = true;
            promotedIcons.Add(notifyIcon);

            DispatcherTimer unpromoteTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(e.Balloon.Timeout + 500) // Keep it around for a few ms for the animation to complete
            };
            unpromoteTimer.Tick += (object sender, EventArgs e) =>
            {
                if (promotedIcons.Contains(notifyIcon))
                {
                    if (!(Settings.Instance.NotifyIconBehaviors.Find(setting => setting.Identifier == notifyIcon.Identifier) is NotifyIconBehaviorSetting iconSetting && iconSetting.Behavior == NotifyIconBehavior.AlwaysShow))
                    {
                        // Don't unpin if settings were changed to always show
                        notifyIcon.IsPinned = false;
                    }
                    promotedIcons.Remove(notifyIcon);
                }
                unpromoteTimer.Stop();
            };
            unpromoteTimer.Start();
        }

        private void NotifyIconList_Loaded(object sender, RoutedEventArgs e)
        {
            SetNotificationAreaCollections();

            // Set up drag/drop handler
            if (dropHandler == null)
            {
                dropHandler = new NotifyIconDropHandler(this);
                GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(NotifyIcons, dropHandler);
            }
        }

        private void NotifyIconList_OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                return;
            }

            if (NotificationArea != null)
            {
                NotificationArea.NotificationBalloonShown -= NotificationArea_NotificationBalloonShown;
                NotificationArea.UnpinnedIcons.CollectionChanged -= UnpinnedIcons_CollectionChanged;
                NotificationArea.UnpinnedIcons.Filter = UnpinnedNotifyIcons_Filter;
                Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            }

            if (collectionView != null)
            {
                ((INotifyCollectionChanged)collectionView).CollectionChanged -= CollectionView_CollectionChanged;
            }

            _isLoaded = false;
        }

        private void UnpinnedIcons_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            SetToggleVisibility();
        }

        private void CollectionView_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            scheduleSystemIconGapUpdate();
        }

        private static bool isSystemIcon(Tray.NotifyIcon icon)
        {
            return icon.IsEqualByIdentifier(Tray.NotificationArea.NETWORK_GUID)
                || icon.IsEqualByIdentifier(Tray.NotificationArea.POWER_GUID)
                || icon.IsEqualByIdentifier(Tray.NotificationArea.VOLUME_GUID);
        }

        private static RetroBar.Controls.NotifyIcon findNotifyIconControl(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (child is RetroBar.Controls.NotifyIcon control)
                {
                    return control;
                }

                RetroBar.Controls.NotifyIcon found = findNotifyIconControl(child);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void scheduleSystemIconGapUpdate()
        {
            if (gapUpdatePending) return;

            gapUpdatePending = true;

            // wait until the icon containers have been generated
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                gapUpdatePending = false;
                updateSystemIconGaps();
            }));
        }


        private void updateSystemIconGaps()
        {
            if (collectionView == null) return;

            double gap = TryFindResource("NotifyIconSystemGap") as double? ?? 0;
            Orientation orientation = (Window.GetWindow(this) as Taskbar)?.Orientation ?? Orientation.Horizontal;
            bool previousWasSystem = true; // never add a gap before the very first icon

            foreach (object item in NotifyIcons.Items)
            {
                if (!(item is Tray.NotifyIcon icon)) continue;

                bool system = isSystemIcon(icon);

                if (NotifyIcons.ItemContainerGenerator.ContainerFromItem(item) is DependencyObject container &&
                    findNotifyIconControl(container) is RetroBar.Controls.NotifyIcon control)
                {
                    control.SetLeadingGap(system && !previousWasSystem ? gap : 0, orientation);
                }

                previousWasSystem = system;
            }
        }

        private void NotifyIconToggleButton_OnClick(object sender, RoutedEventArgs e)
        {
            collectionView?.Refresh();
        }

        private void SetToggleVisibility()
        {
            if (!Settings.Instance.CollapseNotifyIcons) return;

            if (NotificationArea.UnpinnedIcons.IsEmpty)
            {
                NotifyIconToggleButton.Visibility = Visibility.Collapsed;

                if (NotifyIconToggleButton.IsChecked == true)
                {
                    NotifyIconToggleButton.IsChecked = false;
                }
            }
            else
            {
                NotifyIconToggleButton.Visibility = Visibility.Visible;
            }
        }

        public void UpdateIconOrder(IDropInfo dropInfo)
        {
            if (NotificationArea == null || collectionView == null) return;

            var visibleIcons = collectionView.Cast<Tray.NotifyIcon>().ToList();

            if (IsCollapsed())
            {
                // Do not save temporary promoted icons
                visibleIcons = visibleIcons.Where(i => !promotedIcons.Contains(i)).ToList();
            }

            // Update the dragged icon's position in the list
            if (dropInfo.Data is Tray.NotifyIcon draggedIcon)
            {
                int insertIndex = dropInfo.InsertIndex;
                if (insertIndex > 0 && visibleIcons.IndexOf(draggedIcon) < insertIndex && visibleIcons.Remove(draggedIcon))
                {
                    insertIndex--;
                }
                else
                {
                    visibleIcons.Remove(draggedIcon);
                }
                visibleIcons.Insert(insertIndex, draggedIcon);
            }
            else
            {
                return;
            }
            
            // Never overwrite the list to prevent clearing out settings for non-visible icons
            var oldOrder = Settings.Instance.NotifyIconOrder ?? new List<string>();
            var result = new List<string>();
            int replaceIndex = 0;
            
            foreach (var id in oldOrder)
            {
                if (visibleIcons.Find(i => i.IsEqualByIdentifier(id)) != null)
                {
                    if (replaceIndex < visibleIcons.Count)
                    {
                        result.Add(visibleIcons[replaceIndex++].Identifier);
                    }
                }
                else
                {
                    result.Add(id);
                }
            }

            while (replaceIndex < visibleIcons.Count)
            {
                result.Add(visibleIcons[replaceIndex++].Identifier);
            }

            Settings.Instance.NotifyIconOrder = result;
        }

        public class NotifyIconComparer : System.Collections.IComparer
        {
            private NotifyIconList _host;

            public NotifyIconComparer(NotifyIconList host)
            {
                _host = host;
            }

            public int Compare(object x, object y)
            {
                if (x is Tray.NotifyIcon xIcon && y is Tray.NotifyIcon yIcon && Settings.Instance.NotifyIconOrder is List<string> setting)
                {
                    if (_host.IsCollapsed())
                    {
                        bool xPromoted = _host.promotedIcons.Contains(xIcon);
                        bool yPromoted = _host.promotedIcons.Contains(yIcon);
                        if (xPromoted && !yPromoted)
                        {
                            return -1;
                        }
                        if (!xPromoted && yPromoted)
                        {
                            return 1;
                        }
                    }
                    int xIndex = setting.FindIndex(s => xIcon.IsEqualByIdentifier(s));
                    int yIndex = setting.FindIndex(s => yIcon.IsEqualByIdentifier(s));
                    return xIndex.CompareTo(yIndex);
                }
                return 0;
            }
        }
    }
}