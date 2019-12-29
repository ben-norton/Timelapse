﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Timelapse.Database;
using Timelapse.Dialog;
using Timelapse.Enums;
using Timelapse.QuickPaste;
using Timelapse.Util;
using MessageBox = Timelapse.Dialog.MessageBox;

// Edit Menu Callbacks
namespace Timelapse
{
    public partial class TimelapseWindow : Window, IDisposable
    {
        // Edit Submenu Opening 
        private void Edit_SubmenuOpening(object sender, RoutedEventArgs e)
        {
            this.FilePlayer_Stop(); // In case the FilePlayer is going

            // Enable / disable various edit menu items depending on whether we are looking at the single image view or overview
            bool state = this.IsDisplayingSingleImage();
            this.MenuItemCopyPreviousValues.IsEnabled = state;
        }

        // Find image 
        private void MenuItemFindByFileName_Click(object sender, RoutedEventArgs e)
        {
            this.FindBoxSetVisibility(true);
        }

        // Show QuickPaste Window 
        private void MenuItemQuickPasteWindowShow_Click(object sender, RoutedEventArgs e)
        {
            if (this.quickPasteWindow == null)
            {
                // create the quickpaste window if it doesn't already exist.
                this.QuickPasteWindowShow();
            }
            this.QuickPasteRefreshWindowAndXML();
            this.QuickPasteWindowShow();
        }

        // Import QuickPaste Items from .ddb file
        private void MenuItemQuickPasteImportFromDB_Click(object sender, RoutedEventArgs e)
        {
            if (Utilities.TryGetFileFromUser("Import QuickPaste entries by selecting the Timelapse database (.ddb) file from the image folder where you had used them.",
                                             Path.Combine(this.dataHandler.FileDatabase.FolderPath, Constant.File.DefaultFileDatabaseFileName),
                                             String.Format("Database files (*{0})|*{0}", Constant.File.FileDatabaseFileExtension),
                                             Constant.File.FileDatabaseFileExtension,
                                             out string ddbFile) == true)
            {
                List<QuickPasteEntry> qpe = QuickPasteOperations.QuickPasteImportFromDB(this.dataHandler.FileDatabase, ddbFile);
                if (qpe.Count == 0)
                {
                    MessageBox messageBox = new MessageBox("Could not import QuickPaste entries", this);
                    messageBox.Message.Problem = "Timelapse could not find any QuickPaste entries in the selected database";
                    messageBox.Message.Reason = "When an analyst creates QuickPaste entries, those entries are stored in the database file " + Environment.NewLine;
                    messageBox.Message.Reason += "associated with the image set being analyzed. Since none where found, " + Environment.NewLine;
                    messageBox.Message.Reason += "its likely that no one had created any quickpaste entries when analyzing that image set.";
                    messageBox.Message.Hint = "Perhaps they are in a different database?";
                    messageBox.Message.Icon = MessageBoxImage.Information;
                    messageBox.ShowDialog();
                    return;
                }
                else
                {
                    this.quickPasteEntries = qpe;
                    this.dataHandler.FileDatabase.SyncImageSetToDatabase();
                    this.QuickPasteRefreshWindowAndXML();
                    this.QuickPasteWindowShow();
                }
            }
        }

        // Copy Previous Values
        private void MenuItemCopyPreviousValues_Click(object sender, RoutedEventArgs e)
        {
            this.CopyPreviousValues_Click();
        }

        // Populate a data field from metadata (example metadata displayed from the currently selected image)
        private void MenuItemPopulateFieldFromMetadata_Click(object sender, RoutedEventArgs e)
        {
            // If we are not in the selection All view, or if its a corrupt image or deleted image, or if its a video that no longer exists, tell the person. Selecting ok will shift the selection.
            // We want to be on a valid image as otherwise the metadata of interest won't appear
            if (this.dataHandler.ImageCache.Current.IsDisplayable(this.FolderPath) == false)
            {
                // There are no displayable images, and thus no metadata to choose from, so abort
                MessageBox messageBox = new MessageBox("Populate a data field with image metadata of your choosing.", this);
                messageBox.Message.Problem = "Timelapse can't extract any metadata, as the currently displayed image or video is missing or corrupted." + Environment.NewLine;
                messageBox.Message.Reason = "Timelapse tries to examines the currently displayed image or video for its metadata.";
                messageBox.Message.Hint = "Navigate to a displayable image or video, and try again.";
                messageBox.Message.Icon = MessageBoxImage.Error;
                messageBox.ShowDialog();
                return;
            }

            // Warn the user that they are currently in a selection displaying only a subset of files, and make sure they want to continue.
            if (Dialogs.MaybePromptToApplyOperationOnSelection(this, this.dataHandler.FileDatabase, this.State.SuppressSelectedPopulateFieldFromMetadataPrompt,
                                                                           "'Populate a data field with image metadata...'",
                                                               (bool optOut) =>
                                                               {
                                                                   this.State.SuppressSelectedPopulateFieldFromMetadataPrompt = optOut;
                                                               }))
            {
                using (PopulateFieldWithMetadata populateField = new PopulateFieldWithMetadata(this, this.dataHandler.FileDatabase, this.dataHandler.ImageCache.Current.GetFilePath(this.FolderPath)))
                {
                    if (this.ShowDialogAndCheckIfChangesWereMade(populateField))
                    {
                        this.FilesSelectAndShow();
                    };
                }
            }
        }

        // Delete sub-menu opening
        private void MenuItemDelete_SubmenuOpening(object sender, RoutedEventArgs e)
        {
            try
            {
                int deletedImages = this.dataHandler.FileDatabase.GetFileCount(FileSelectionEnum.MarkedForDeletion);
                this.MenuItemDeleteFiles.IsEnabled = deletedImages > 0;
                this.MenuItemDeleteFilesAndData.IsEnabled = deletedImages > 0;
                this.MenuItemDeleteCurrentFileAndData.IsEnabled = true;
                ImageRow imageRow = this.dataHandler.ImageCache.Current;

                this.MenuItemDeleteCurrentFile.IsEnabled = this.dataHandler.ImageCache.Current.IsDisplayable(this.FolderPath);
            }
            catch (Exception exception)
            {
                TraceDebug.PrintMessage(String.Format("Delete submenu failed to open in Delete_SubmenuOpening. {0}", exception.ToString()));

                // This function was blowing up on one user's machine, but not others.
                // I couldn't figure out why, so I just put this fallback in here to catch that unusual case.
                this.MenuItemDeleteFiles.IsEnabled = true;
                this.MenuItemDeleteFilesAndData.IsEnabled = true;
                this.MenuItemDeleteCurrentFile.IsEnabled = true;
                this.MenuItemDeleteCurrentFileAndData.IsEnabled = true;
            }
        }

        // Delete callback manages all deletion menu choices where: 
        // - the current image or all images marked for deletion are deleted
        // - the data associated with those images may be delted.
        // - deleted images are moved to a backup folder.
        private void MenuItemDeleteFiles_Click(object sender, RoutedEventArgs e)
        {
            MenuItem menuItem = sender as MenuItem;

            // This callback is invoked by DeleteImage (which deletes the current image) and DeleteImages (which deletes the images marked by the deletion flag)
            // Thus we need to use two different methods to construct a table containing all the images marked for deletion
            List<ImageRow> filesToDelete;
            bool deleteCurrentImageOnly;
            bool deleteFilesAndData;
            if (menuItem.Name.Equals(this.MenuItemDeleteFiles.Name) || menuItem.Name.Equals(this.MenuItemDeleteFilesAndData.Name))
            {
                deleteCurrentImageOnly = false;
                deleteFilesAndData = menuItem.Name.Equals(this.MenuItemDeleteFilesAndData.Name);
                // get list of all images marked for deletion in the current seletion
                using (FileTable filetable = this.dataHandler.FileDatabase.GetFilesMarkedForDeletion())
                {
                    filesToDelete = filetable.ToList();
                }

                for (int index = filesToDelete.Count - 1; index >= 0; index--)
                {
                    if (this.dataHandler.FileDatabase.FileTable.Find(filesToDelete[index].ID) == null)
                    {
                        filesToDelete.Remove(filesToDelete[index]);
                    }
                }
            }
            else
            {
                // Delete current image case. Get the ID of the current image and construct a datatable that contains that image's datarow
                deleteCurrentImageOnly = true;
                deleteFilesAndData = menuItem.Name.Equals(this.MenuItemDeleteCurrentFileAndData.Name);
                filesToDelete = new List<ImageRow>();
                if (this.dataHandler.ImageCache.Current != null)
                {
                    filesToDelete.Add(this.dataHandler.ImageCache.Current);
                }
            }

            // We have to change the way the current image is displayed, as otherwise it cannot be deleted as there is still a reference to the file.
            // NOTE THAT WE NEED TO DO THIS FOR VIDEOS AND FOR MULTIPLEIMAGEVIEW
            // MAYBE CLEAR THE IMAGE CACHE TOO? 
            this.dataHandler.ImageCache.Current.GetBitmapFromFile(this.FolderPath, 128, ImageDisplayIntentEnum.TransientNavigating, out _);

            // If no images are selected for deletion. Warn the user.
            // Note that this should never happen, as the invoking menu item should be disabled (and thus not selectable)
            // if there aren't any images to delete. Still,...
            if (filesToDelete == null || filesToDelete.Count < 1)
            {
                MessageBox messageBox = new MessageBox("No files are marked for deletion", this);
                messageBox.Message.Problem = "You are trying to delete files marked for deletion, but no files have their 'Delete?' field checked.";
                messageBox.Message.Hint = "If you have files that you think should be deleted, check their Delete? field.";
                messageBox.Message.Icon = MessageBoxImage.Information;
                messageBox.ShowDialog();
                return;
            }
            long currentFileID = this.dataHandler.ImageCache.Current.ID;
            DeleteImages deleteImagesDialog = new DeleteImages(this, this.dataHandler.FileDatabase, this.dataHandler.ImageCache, this.MarkableCanvas, filesToDelete, deleteFilesAndData, deleteCurrentImageOnly);
            bool? result = deleteImagesDialog.ShowDialog();
            if (result == true)
            {
                // Delete the files
                Mouse.OverrideCursor = Cursors.Wait;
                // Reload the file datatable. 
                this.FilesSelectAndShow(currentFileID, this.dataHandler.FileDatabase.ImageSet.FileSelection);

                if (deleteFilesAndData)
                {
                    // Find and show the image closest to the last one shown
                    if (this.dataHandler.FileDatabase.CurrentlySelectedFileCount > 0)
                    {
                        int nextImageRow = this.dataHandler.FileDatabase.GetFileOrNextFileIndex(currentFileID);
                        this.FileShow(nextImageRow);
                    }
                    else
                    {
                        // No images left, so disable everything
                        this.EnableOrDisableMenusAndControls();
                    }
                }
                else
                {
                    // display the updated properties on the current image, or the closest one to it.
                    int nextImageRow = this.dataHandler.FileDatabase.FindClosestImageRow(currentFileID);
                    this.FileShow(nextImageRow);
                }
                Mouse.OverrideCursor = null;
            }
        }

        // Date Correction sub-menu opening
        private void MenuItemDateCorrection_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (this.IsUTCOffsetControlHidden())
            {
                this.MenuItemSetTimeZone.IsEnabled = false;
            }
        }

        // Re-read dates and times from files
        private void MenuItemRereadDateTimesfromFiles_Click(object sender, RoutedEventArgs e)
        {
            // Warn the user that they are currently in a selection displaying only a subset of files, and make sure they want to continue.
            if (Dialogs.MaybePromptToApplyOperationOnSelection(
                this,
                this.dataHandler.FileDatabase,
                this.State.SuppressSelectedRereadDatesFromFilesPrompt,
                "'Reread dates and times from files...'",
                (bool optOut) => { this.State.SuppressSelectedRereadDatesFromFilesPrompt = optOut; }
                ))
            {
                DateTimeRereadFromFiles rereadDates = new DateTimeRereadFromFiles(this, this.dataHandler.FileDatabase);
                if (this.ShowDialogAndCheckIfChangesWereMade(rereadDates))
                {
                    this.FilesSelectAndShow();
                };
            }
        }

        // Correct for daylight savings time
        private void MenuItemDaylightSavingsTimeCorrection_Click(object sender, RoutedEventArgs e)
        {
            // Warn the user that they are currently in a selection displaying only a subset of files, and make sure they want to continue.
            if (Dialogs.MaybePromptToApplyOperationOnSelection(
                this,
                this.dataHandler.FileDatabase,
                this.State.SuppressSelectedDaylightSavingsCorrectionPrompt,
                "'Correct for daylight savings time...'",
                (bool optOut) => { this.State.SuppressSelectedDaylightSavingsCorrectionPrompt = optOut; }
                ))
            {
                DateDaylightSavingsTimeCorrection dateTimeChange = new DateDaylightSavingsTimeCorrection(this, this.dataHandler.FileDatabase, this.dataHandler.ImageCache);
                if (this.ShowDialogAndCheckIfChangesWereMade(dateTimeChange))
                {
                    this.FilesSelectAndShow();
                };
            }
        }

        // Correct for cameras not set to the right date and time by specifying an offset
        private void MenuItemDateTimeFixedCorrection_Click(object sender, RoutedEventArgs e)
        {
            // Warn the user that they are currently in a selection displaying only a subset of files, and make sure they want to continue.
            if (Dialogs.MaybePromptToApplyOperationOnSelection(this, this.dataHandler.FileDatabase, this.State.SuppressSelectedDateTimeFixedCorrectionPrompt,
                                                                           "'Add a fixed correction value to every date/time...'",
                                                               (bool optOut) =>
                                                               {
                                                                   this.State.SuppressSelectedDateTimeFixedCorrectionPrompt = optOut;
                                                               }))
            {
                DateTimeFixedCorrection fixedDateCorrection = new DateTimeFixedCorrection(this, this.dataHandler.FileDatabase, this.dataHandler.ImageCache.Current);
                if (this.ShowDialogAndCheckIfChangesWereMade(fixedDateCorrection))
                {
                    this.FilesSelectAndShow();
                }
            }
        }

        // Correct for cameras whose clock runs fast or slow (clock drift). 
        // Note that the correction is applied only to images in the selected view.
        private void MenuItemDateTimeLinearCorrection_Click(object sender, RoutedEventArgs e)
        {
            // Warn the user that they are currently in a selection displaying only a subset of files, and make sure they want to continue.
            if (Dialogs.MaybePromptToApplyOperationOnSelection(
                this,
                this.dataHandler.FileDatabase,
                this.State.SuppressSelectedDateTimeLinearCorrectionPrompt,
                "'Correct for camera clock drift'",
                (bool optOut) => { this.State.SuppressSelectedDateTimeLinearCorrectionPrompt = optOut; }
                ))
            {
                DateTimeLinearCorrection linearDateCorrection = new DateTimeLinearCorrection(this, this.dataHandler.FileDatabase);
                if (this.ShowDialogAndCheckIfChangesWereMade(linearDateCorrection))
                {
                    this.FilesSelectAndShow();
                }
            }
        }

        // Correct ambiguous dates dialog i.e. dates that could be read as either month/day or day/month
        private void MenuItemCorrectAmbiguousDates_Click(object sender, RoutedEventArgs e)
        {
            // Warn the user that they are currently in a selection displaying only a subset of files, and make sure they want to continue.
            if (Dialogs.MaybePromptToApplyOperationOnSelection(
                this, this.dataHandler.FileDatabase, this.State.SuppressSelectedAmbiguousDatesPrompt,
                "'Correct ambiguous dates...'",
                (bool optOut) =>
                 {
                     this.State.SuppressSelectedAmbiguousDatesPrompt = optOut;
                 }))
            {
                DateCorrectAmbiguous dateCorrection = new DateCorrectAmbiguous(this, this.dataHandler.FileDatabase);
                if (this.ShowDialogAndCheckIfChangesWereMade(dateCorrection))
                {
                    this.FilesSelectAndShow();
                }
            }
        }

        // Reassign a group of images to a particular time zone
        private void MenuItemSetTimeZone_Click(object sender, RoutedEventArgs e)
        {
            // Warn the user that they are currently in a selection displaying only a subset of files, and make sure they want to continue.
            if (Dialogs.MaybePromptToApplyOperationOnSelection(this, this.dataHandler.FileDatabase, this.State.SuppressSelectedSetTimeZonePrompt,
                                                                           "'Set the time zone of every date/time...'",
                                                               (bool optOut) =>
                                                               {
                                                                   this.State.SuppressSelectedSetTimeZonePrompt = optOut;
                                                               }))
            {
                DateTimeSetTimeZone fixedDateCorrection = new DateTimeSetTimeZone(this.dataHandler.FileDatabase, this.dataHandler.ImageCache.Current, this);
                if (this.ShowDialogAndCheckIfChangesWereMade(fixedDateCorrection))
                {
                    this.FilesSelectAndShow();
                }
            }
        }

        // Identify or reclassify dark files.
        private void MenuItemEditClassifyDarkImages_Click(object sender, RoutedEventArgs e)
        {
            // Warn the user that they are currently in a selection displaying only a subset of files, and make sure they want to continue.
            if (Dialogs.MaybePromptToApplyOperationOnSelection(this, this.dataHandler.FileDatabase, this.State.SuppressSelectedDarkThresholdPrompt,
                                                                           "'(Re-) classify dark files...'",
                                                               (bool optOut) =>
                                                               {
                                                                   this.State.SuppressSelectedDarkThresholdPrompt = optOut; // SG TODO
                                                               }))
            {
                using (DarkImagesThreshold darkThreshold = new DarkImagesThreshold(this.dataHandler.FileDatabase, this.dataHandler.ImageCache.CurrentRow, this.State, this))
                {
                    darkThreshold.Owner = this;
                    darkThreshold.ShowDialog();
                    // Force an update of the current image in case the current values have changed
                    this.FileShow(this.dataHandler.ImageCache.CurrentRow, true);
                }
            }
        }

        // Edit notes for this image set
        private void MenuItemLog_Click(object sender, RoutedEventArgs e)
        {
            EditLog editImageSetLog = new EditLog(this.dataHandler.FileDatabase.ImageSet.Log, this)
            {
                Owner = this
            };
            bool? result = editImageSetLog.ShowDialog();
            if (result == true)
            {
                this.dataHandler.FileDatabase.ImageSet.Log = editImageSetLog.Log.Text;
                this.dataHandler.FileDatabase.SyncImageSetToDatabase();
            }
        }

        // HELPER FUNCTION, only referenced by the above menu callbacks.
        // Various dialogs perform a bulk edit, after which various states have to be refreshed
        // This method shows the dialog and (if a bulk edit is done) refreshes those states.
        private bool ShowDialogAndCheckIfChangesWereMade(Window dialog)
        {
            dialog.Owner = this;
            return (dialog.ShowDialog() == true);
        }
    }
}
