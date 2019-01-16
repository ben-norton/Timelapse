﻿using System;
using System.Windows;
using Timelapse.Dialog;

namespace Timelapse
{
    // Options Menu Callbacks
    public partial class TimelapseWindow : Window, IDisposable
    {
        // Options sub-menu opening
        private void Options_SubmenuOpening(object sender, RoutedEventArgs e)
        {
            FilePlayer_Stop(); // In case the FilePlayer is going
        }

        // Audio feedback: toggle on / off
        private void MenuItemAudioFeedback_Click(object sender, RoutedEventArgs e)
        {
            // We don't have to do anything here...
            this.state.AudioFeedback = !this.state.AudioFeedback;
            this.MenuItemAudioFeedback.IsChecked = this.state.AudioFeedback;
        }

        // Display Magnifier: toggle on / off
        private void MenuItemDisplayMagnifyingGlass_Click(object sender, RoutedEventArgs e)
        {
            this.dataHandler.FileDatabase.ImageSet.MagnifyingGlassEnabled = !this.dataHandler.FileDatabase.ImageSet.MagnifyingGlassEnabled;
            this.MarkableCanvas.MagnifyingGlassEnabled = this.dataHandler.FileDatabase.ImageSet.MagnifyingGlassEnabled;
            this.MenuItemDisplayMagnifyingGlass.IsChecked = this.dataHandler.FileDatabase.ImageSet.MagnifyingGlassEnabled;
        }

        // Increase magnification of the magnifying glass. 
        private void MenuItemMagnifyingGlassIncrease_Click(object sender, RoutedEventArgs e)
        {
            // Increase the magnification by several steps to make
            // the effect more visible through a menu option versus the keyboard equivalent
            for (int i = 0; i < 6; i++)
            {
                this.MarkableCanvas.MagnifierZoomIn();
            }
        }

        // Decrease the magnification of the magnifying glass. 
        private void MenuItemMagnifyingGlassDecrease_Click(object sender, RoutedEventArgs e)
        {
            // Decrease the magnification by several steps to make
            // the effect more visible through a menu option versus the keyboard equivalent
            for (int i = 0; i < 6; i++)
            {
                this.MarkableCanvas.MagnifierZoomOut();
            }
        }

        // Adjust FilePlayer playback speeds
        private void MenuItemFilePlayerOptions_Click(object sender, RoutedEventArgs e)
        {
            FilePlayerOptions filePlayerOptions = new FilePlayerOptions(this.state, this);
            filePlayerOptions.ShowDialog();
        }

        // Hide or show various informational dialogs"
        private void MenuItemDialogsOnOrOff_Click(object sender, RoutedEventArgs e)
        {
            DialogsHideOrShow dialog = new DialogsHideOrShow(this.state, this);
            dialog.ShowDialog();
        }

        // Classify dark images automatically on initial load
        private void MenuItemClassifyDarkImagesWhenLoading_Click(object sender, RoutedEventArgs e)
        {
            DarkImagesClassifyAutomatically darkImagesOptions = new DarkImagesClassifyAutomatically(this.state, this);
            darkImagesOptions.ShowDialog();
            this.MenuItemClassifyDarkImagesWhenLoading.IsChecked = this.state.ClassifyDarkImagesWhenLoading;
        }

        /// <summary>Show advanced Timelapse options</summary>
        private void MenuItemAdvancedTimelapseOptions_Click(object sender, RoutedEventArgs e)
        {
            AdvancedTimelapseOptions advancedTimelapseOptions = new AdvancedTimelapseOptions(this.state, this.MarkableCanvas, this);
            advancedTimelapseOptions.ShowDialog();
        }

        #region Depracated menu items
        // Depracated
        // private void MenuItemAdvancedImageSetOptions_Click(object sender, RoutedEventArgs e)
        // {
        //    AdvancedImageSetOptions advancedImageSetOptions = new AdvancedImageSetOptions(this.dataHandler.FileDatabase, this);
        //    advancedImageSetOptions.ShowDialog();
        // }

        // Depracated
        // SaulXXX This is a temporary function to allow a user to check for and to delete any duplicate records.
        // private void MenuItemDeleteDuplicates_Click(object sender, RoutedEventArgs e)
        // {
        //    // Warn user that they are in a selected view, and verify that they want to continue
        //    if (this.dataHandler.FileDatabase.ImageSet.FileSelection != FileSelection.All)
        //    {
        //        // Need to be viewing all files
        //        MessageBox messageBox = new MessageBox("You need to select All Files before deleting duplicates", this);
        //        messageBox.Message.Problem = "Delete Duplicates should be applied to All Files, but you only have a subset selected";
        //        messageBox.Message.Solution = "On the Select menu, choose 'All Files' and try again";
        //        messageBox.Message.Icon = MessageBoxImage.Exclamation;
        //        messageBox.ShowDialog();
        //        return;
        //    }
        //    else
        //    {
        //        // Generate a list of duplicate rows showing their filenames (including relative path) 
        //        List<string> filenames = new List<string>();
        //        FileTable table = this.dataHandler.FileDatabase.GetDuplicateFiles();
        //        if (table != null && table.Count() != 0)
        //        {
        //            // populate the list
        //            foreach (ImageRow image in table)
        //            {
        //                string separator = String.IsNullOrEmpty(image.RelativePath) ? "" : "/";
        //                filenames.Add(image.RelativePath + separator + image.FileName);
        //            }
        //        }

        // // Raise a dialog box that shows the duplicate files (if any), where the user needs to confirm their deletion
        //        DeleteDuplicates deleteDuplicates = new DeleteDuplicates(this, filenames);
        //        bool? result = deleteDuplicates.ShowDialog();
        //        if (result == true)
        //        {
        //            // Delete the duplicate files
        //            this.dataHandler.FileDatabase.DeleteDuplicateFiles();
        //            // Reselect on the current select settings, which updates the view to remove the deleted files
        //            this.SelectFilesAndShowFile();
        //        }
        //    }
        // }
        #endregion
    }
}