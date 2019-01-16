﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Timelapse.Database;
using Timelapse.Enums;

// Selection Menu Callbacks
namespace Timelapse
{
    // Select Menu Callbacks
    public partial class TimelapseWindow : Window, IDisposable
    {
        // Select sub-menu opening
        private void MenuItemSelect_SubmenuOpening(object sender, RoutedEventArgs e)
        {
            FilePlayer_Stop(); // In case the FilePlayer is going
            Dictionary<FileSelectionEnum, int> counts = this.dataHandler.FileDatabase.GetFileCountsBySelection();

            // Enable only the menu items that can select at least one potential image 
            this.MenuItemSelectLightFiles.IsEnabled = counts[FileSelectionEnum.Ok] > 0;
            this.MenuItemSelectDarkFiles.IsEnabled = counts[FileSelectionEnum.Dark] > 0;
            this.MenuItemSelectCorruptedFiles.IsEnabled = counts[FileSelectionEnum.Corrupted] > 0;
            this.MenuItemSelectFilesNoLongerAvailable.IsEnabled = counts[FileSelectionEnum.Missing] > 0;
            this.MenuItemSelectFilesMarkedForDeletion.IsEnabled = this.dataHandler.FileDatabase.GetFileCount(FileSelectionEnum.MarkedForDeletion) > 0;

            // Put a checkmark next to the menu item that matches the stored selection criteria
            FileSelectionEnum selection = this.dataHandler.FileDatabase.ImageSet.FileSelection;
            this.MenuItemSelectAllFiles.IsChecked = selection == FileSelectionEnum.All;
            this.MenuItemSelectCorruptedFiles.IsChecked = selection == FileSelectionEnum.Corrupted;
            this.MenuItemSelectDarkFiles.IsChecked = selection == FileSelectionEnum.Dark;
            this.MenuItemSelectLightFiles.IsChecked = selection == FileSelectionEnum.Ok;
            this.MenuItemSelectFilesNoLongerAvailable.IsChecked = selection == FileSelectionEnum.Missing;
            this.MenuItemSelectFilesMarkedForDeletion.IsChecked = selection == FileSelectionEnum.MarkedForDeletion;
            this.MenuItemSelectCustomSelection.IsChecked = selection == FileSelectionEnum.Custom;
        }

        // Select callback: handles all standard menu selection items
        private void MenuItemSelectFiles_Click(object sender, RoutedEventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            FileSelectionEnum selection;

            // find out which selection was selected
            if (item == this.MenuItemSelectAllFiles)
            {
                selection = FileSelectionEnum.All;
            }
            else if (item == this.MenuItemSelectLightFiles)
            {
                selection = FileSelectionEnum.Ok;
            }
            else if (item == this.MenuItemSelectCorruptedFiles)
            {
                selection = FileSelectionEnum.Corrupted;
            }
            else if (item == this.MenuItemSelectDarkFiles)
            {
                selection = FileSelectionEnum.Dark;
            }
            else if (item == this.MenuItemSelectFilesNoLongerAvailable)
            {
                selection = FileSelectionEnum.Missing;
            }
            else if (item == this.MenuItemSelectFilesMarkedForDeletion)
            {
                selection = FileSelectionEnum.MarkedForDeletion;
            }
            else
            {
                selection = FileSelectionEnum.All;   // Just in case
            }

            // Treat the checked status as a radio button i.e., toggle their states so only the clicked menu item is checked.
            this.SelectFilesAndShowFile(this.dataHandler.ImageCache.Current.ID, selection);  // Go to the first result (i.e., index 0) in the given selection set
        }

        // Custom Selection: raises a dialog letting the user specify their selection criteria
        private void MenuItemSelectCustomSelection_Click(object sender, RoutedEventArgs e)
        {
            // the first time the custom selection dialog is launched update the DateTime and UtcOffset search terms to the time of the current image
            SearchTerm firstDateTimeSearchTerm = this.dataHandler.FileDatabase.CustomSelection.SearchTerms.First(searchTerm => searchTerm.DataLabel == Constant.DatabaseColumn.DateTime);
            if (firstDateTimeSearchTerm.GetDateTime() == Constant.ControlDefault.DateTimeValue.DateTime)
            {
                DateTimeOffset defaultDate = this.dataHandler.ImageCache.Current.GetDateTime();
                this.dataHandler.FileDatabase.CustomSelection.SetDateTimesAndOffset(defaultDate);
            }

            // show the dialog and process the resuls
            Dialog.CustomSelection customSelection = new Dialog.CustomSelection(this.dataHandler.FileDatabase, this, this.IsUTCOffsetControlHidden())
            {
                Owner = this
            };
            bool? changeToCustomSelection = customSelection.ShowDialog();
            // Set the selection to show all images and a valid image
            if (changeToCustomSelection == true)
            {
                this.SelectFilesAndShowFile(this.dataHandler.ImageCache.Current.ID, FileSelectionEnum.Custom);
            }
            else
            {
                // Since we canceled the custom selection, uncheck the item (but only if another menu item is shown checked)

                bool otherMenuItemIsChecked =
                    this.MenuItemSelectAllFiles.IsChecked ||
                    this.MenuItemSelectCorruptedFiles.IsChecked ||
                    this.MenuItemSelectDarkFiles.IsChecked ||
                    this.MenuItemSelectLightFiles.IsChecked ||
                    this.MenuItemSelectFilesNoLongerAvailable.IsChecked ||
                    this.MenuItemSelectFilesMarkedForDeletion.IsChecked;
                this.MenuItemSelectCustomSelection.IsChecked = otherMenuItemIsChecked ? false : true;
            }
        }

        // Show file counts: how many images were loaded, types in categories, etc.
        public void MenuItemImageCounts_Click(object sender, RoutedEventArgs e)
        {
            this.MaybeShowFileCountsDialog(false, this);
        }

        // Refresh the selection: based on the current select criteria. 
        // Useful when, for example, the user has selected a view, but then changed some data values where items no longer match the current selection.
        private void MenuItemSelectReselect_Click(object sender, RoutedEventArgs e)
        {
            // Reselect the images, which re-sorts them to the current sort criteria. 
            this.SelectFilesAndShowFile(this.dataHandler.ImageCache.Current.ID, this.dataHandler.FileDatabase.ImageSet.FileSelection, true);
        }
    }
}