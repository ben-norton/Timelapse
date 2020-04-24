﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using Timelapse.Database;
using Timelapse.Enums;
using Timelapse.Util;
using Clipboard = System.Windows.Clipboard;
using Rectangle = System.Drawing.Rectangle;

namespace Timelapse.Dialog
{
    public static class Dialogs
    {
        #region Dialog Messages: Corrupted .DDB file (no primary key)
        #endregion
        #region Dialog Box Positioning and Fitting
        // Most (but not all) invocations of SetDefaultDialogPosition and TryFitDialogWndowInWorkingArea 
        // are done together, so collapse it into a single call
        public static void TryPositionAndFitDialogIntoWindow(Window window)
        {
            Dialogs.SetDefaultDialogPosition(window);
            Dialogs.TryFitDialogInWorkingArea(window);
        }


        // Position the dialog box within its owner's window
        public static void SetDefaultDialogPosition(Window window)
        {
            // Check the arguments for null 
            if (window == null)
            {
                // this should not happen
                TracePrint.PrintStackTrace("Window's owner property is null. Is a set of it prior to calling ShowDialog() missing?", 1);
                // Treat it as a no-op
                return;
            }

            window.Left = window.Owner.Left + (window.Owner.Width - window.ActualWidth) / 2; // Center it horizontally
            window.Top = window.Owner.Top + 20; // Offset it from the windows'top by 20 pixels downwards
        }

        // Used to ensure that the window is positioned within the screen
        // Note that all uses of this method is by dialog box windows (which should be initialy positioned relative to the main timelapse window) by a call to SetDefaultDialogPosition), 
        // rather than the main timelapse window (whose position, size and layout  is managed by the TimelapseAvalonExtension methods). 
        // We could likely collapse the two, but its not worth the bother. 
        public static bool TryFitDialogInWorkingArea(Window window)
        {
            if (window == null)
            {
                return false;
            }
            if (Double.IsNaN(window.Left))
            {
                window.Left = 0;
            }
            if (Double.IsNaN(window.Top))
            {
                window.Top = 0;
            }

            // If needed, adjust the window's height to be somewhat smaller than the screen 
            // We allow some space for the task bar, assuming its visible at the screen's bottom
            // and place the window at the very top. Note that this won't cater for the situation when
            // the task bar is at the top of the screen, but so it goes.
            int typicalTaskBarHeight = 40;
            double availableScreenHeight = System.Windows.SystemParameters.PrimaryScreenHeight - typicalTaskBarHeight;
            if (window.Height > availableScreenHeight)
            {
                window.Height = availableScreenHeight;
                window.Top = 0;
            }

            Rectangle windowPosition = new Rectangle((int)window.Left, (int)window.Top, (int)window.Width, (int)window.Height);
            Rectangle workingArea = Screen.GetWorkingArea(windowPosition);
            bool windowFitsInWorkingArea = true;

            // move window up if it extends below the working area
            if (windowPosition.Bottom > workingArea.Bottom)
            {
                int pixelsToMoveUp = windowPosition.Bottom - workingArea.Bottom;
                if (pixelsToMoveUp > windowPosition.Top)
                {
                    // window is too tall and has to shorten to fit screen
                    window.Top = 0;
                    window.Height = workingArea.Bottom;
                    windowFitsInWorkingArea = false;
                }
                else if (pixelsToMoveUp > 0)
                {
                    // move window up
                    window.Top -= pixelsToMoveUp;
                }
            }

            // move window left if it extends right of the working area
            if (windowPosition.Right > workingArea.Right)
            {
                int pixelsToMoveLeft = windowPosition.Right - workingArea.Right;
                if (pixelsToMoveLeft > windowPosition.Left)
                {
                    // window is too wide and has to narrow to fit screen
                    window.Left = 0;
                    window.Width = workingArea.Width;
                    windowFitsInWorkingArea = false;
                }
                else if (pixelsToMoveLeft > 0)
                {
                    // move window left
                    window.Left -= pixelsToMoveLeft;
                }
            }
            return windowFitsInWorkingArea;
        }
        #endregion

        #region Dialog Messages: Prompt to apply operation if partial selection.

        // Warn the user that they are currently in a selection displaying only a subset of files, and make sure they want to continue.
        public static bool MaybePromptToApplyOperationOnSelection(Window window, FileDatabase fileDatabase, bool promptState, string operationDescription, Action<bool> persistOptOut)
        {
            if (Dialogs.CheckIfPromptNeeded(promptState, fileDatabase, out int filesTotalCount, out int filesSelectedCount) == false)
            {
                // if showing all images, or if users had elected not to be warned, then no need for showing the warning message
                return true;
            }

            // Warn the user that the operation will only be applied to an image set.
            string title = "Apply " + operationDescription + " to this selection?";
            MessageBox messageBox = new MessageBox(title, window, MessageBoxButton.OKCancel);

            messageBox.Message.What = operationDescription + " will be applied only to a subset of your images." + Environment.NewLine;
            messageBox.Message.What += "Is this what you want?";

            messageBox.Message.Reason = String.Format("A 'selection' is active, where you are currently viewing {0}/{1} total files.{2}", filesSelectedCount, filesTotalCount, Environment.NewLine);
            messageBox.Message.Reason += "Only these selected images will be affected by this operation." + Environment.NewLine;
            messageBox.Message.Reason += "Data for other unselected images will be unaffected.";

            messageBox.Message.Solution = "Select " + Environment.NewLine;
            messageBox.Message.Solution += "\u2022 'Ok' for Timelapse to continue to " + operationDescription + " for these selected files" + Environment.NewLine;
            messageBox.Message.Solution += "\u2022 'Cancel' to abort";

            messageBox.Message.Hint = "This is not an error." + Environment.NewLine;
            messageBox.Message.Hint += "\u2022 We are just reminding you that you have an active selection that is displaying only a subset of your images." + Environment.NewLine;
            messageBox.Message.Hint += "\u2022 You can apply this operation to that subset ." + Environment.NewLine;
            messageBox.Message.Hint += "\u2022 However, if you did want to do this operaton for all images, choose the 'Select|All files' menu option.";

            messageBox.Message.Icon = MessageBoxImage.Question;
            messageBox.DontShowAgain.Visibility = Visibility.Visible;

            bool proceedWithOperation = (bool)messageBox.ShowDialog();
            if (proceedWithOperation && messageBox.DontShowAgain.IsChecked.HasValue && persistOptOut != null)
            {
                persistOptOut(messageBox.DontShowAgain.IsChecked.Value);
            }
            return proceedWithOperation;
        }

        // Check if a prompt dialog is needed
        private static bool CheckIfPromptNeeded(bool promptState, FileDatabase fileDatabase, out int filesTotalCount, out int filesSelectedCount)
        {
            filesTotalCount = 0;
            filesSelectedCount = 0;
            if (fileDatabase == null)
            {
                // This should not happen. Maybe raise an exception?
                // In any case, don't show the prompt
                return false;
            }

            if (promptState)
            {
                // We don't show the prompt as the user has turned it off.
                return false;
            }
            // We want to show the prompt only if the promptState is true, and we are  viewing all images
            filesTotalCount = fileDatabase.CountAllFilesMatchingSelectionCondition(FileSelectionEnum.All);
            filesSelectedCount = fileDatabase.FileTable.RowCount;
            return filesTotalCount != filesSelectedCount;
        }

        #endregion

        #region Dialog Messages: Missing dependencies
        public static void DependencyFilesMissingDialog(string applicationName)
        {
            // can't use DialogMessageBox to show this message as that class requires the Timelapse window to be displayed.
            string messageTitle = String.Format("{0} needs to be in its original downloaded folder.", applicationName);
            StringBuilder message = new StringBuilder("Problem:" + Environment.NewLine);
            message.AppendFormat("{0} won't run properly as it was not correctly installed.{1}{1}", applicationName, Environment.NewLine);
            message.AppendLine("Reason:");
            message.AppendFormat("When you downloaded {0}, it was in a folder with several other files and folders it needs. You probably dragged {0} out of that folder.{1}{1}", applicationName, Environment.NewLine);
            message.AppendLine("Solution:");
            message.AppendFormat("Move the {0} program back to its original folder, or download it again.{1}{1}", applicationName, Environment.NewLine);
            message.AppendLine("Hint:");
            message.AppendFormat("Create a shortcut if you want to access {0} outside its folder:{1}", applicationName, Environment.NewLine);
            message.AppendLine("1. From its original folder, right-click the Timelapse program icon.");
            message.AppendLine("2. Select 'Create Shortcut' from the menu.");
            message.Append("3. Drag the shortcut icon to the location of your choice.");
            System.Windows.MessageBox.Show(message.ToString(), messageTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        #endregion

        #region Dialog Messages: Path too long warnings
        // This version is for hard crashes. however, it may disappear from display too fast as the program will be shut down.
        public static void FilePathTooLongDialog(UnhandledExceptionEventArgs e, Window owner)
        {
            string title = "Your File Path Names are Too Long to Handle";
            MessageBox messageBox = new MessageBox(title, owner);
            messageBox.Message.Icon = MessageBoxImage.Error;
            messageBox.Message.Title = title;
            messageBox.Message.Problem = "Timelapse has to shut down as one or more of your file paths are too long.";
            messageBox.Message.Solution = "\u2022 Shorten the path name by moving your image folder higher up the folder hierarchy, or" + Environment.NewLine + "\u2022 Use shorter folder or file names.";
            messageBox.Message.Reason = "Windows cannot perform file operations if the folder path combined with the file name is more than " + Constant.File.MaxPathLength.ToString() + " characters.";
            messageBox.Message.Result = "Timelapse will shut down until you fix this.";
            messageBox.Message.Hint = "Files created in your " + Constant.File.BackupFolder + " folder must also be less than " + Constant.File.MaxPathLength.ToString() + " characters.";
            if (e != null)
            {
                Clipboard.SetText(e.ExceptionObject.ToString());
            }
            messageBox.ShowDialog();
        }

        // This version detects and displays warning messages.
        public static void FilePathTooLongDialog(List<string> folders, Window owner)
        {
            ThrowIf.IsNullArgument(folders, nameof(folders));

            string title = "Some of your Image File Path Names Were Too Long";
            MessageBox messageBox = new MessageBox(title, owner);
            messageBox.Message.Icon = MessageBoxImage.Error;
            messageBox.Message.Title = title;
            messageBox.Message.Problem = "Timelapse skipped reading some of your images in the folders below, as their file paths were too long.";
            if (folders.Count > 0)
            {
                messageBox.Message.Problem += "Those files are found in these folders:";
                foreach (string folder in folders)
                {
                    messageBox.Message.Problem += Environment.NewLine + "\u2022 " + folder;
                }
            }
            messageBox.Message.Reason = "Windows cannot perform file operations if the folder path combined with the file name is more than " + Constant.File.MaxPathLength.ToString() + " characters.";
            messageBox.Message.Solution = "Try reloading this image set after shortening the file path:" + Environment.NewLine;
            messageBox.Message.Solution += "\u2022 shorten the path name by moving your image folder higher up the folder hierarchy, or" + Environment.NewLine + "\u2022 use shorter folder or file names.";

            messageBox.Message.Hint = "Files created in your " + Constant.File.BackupFolder + " folder must also be less than " + Constant.File.MaxPathLength.ToString() + " characters.";
            messageBox.ShowDialog();
        }

        // notify the user when the path is too long
        public static void TemplatePathTooLongDialog(string templateDatabasePath, Window owner)
        {
            MessageBox messageBox = new MessageBox("Timelapse could not open the template ", owner);
            messageBox.Message.Problem = "Timelapse could not open the Template File as its name is too long:" + Environment.NewLine;
            messageBox.Message.Problem += "\u2022 " + templateDatabasePath;
            messageBox.Message.Reason = "Windows cannot perform file operations if the folder path combined with the file name is more than " + Constant.File.MaxPathLength.ToString() + " characters.";
            messageBox.Message.Solution = "\u2022 Shorten the path name by moving your image folder higher up the folder hierarchy, or" + Environment.NewLine + "\u2022 Use shorter folder or file names.";
            messageBox.Message.Hint = "Files created in your " + Constant.File.BackupFolder + " folder must also be less than " + Constant.File.MaxPathLength.ToString() + " characters.";
            messageBox.Message.Icon = MessageBoxImage.Error;
            messageBox.ShowDialog();
        }

        // notify the user the template couldn't be loaded because its path is too long
        public static void DatabasePathTooLongDialog(string databasePath, Window owner)
        {
            MessageBox messageBox = new MessageBox("Timelapse could not load the database ", owner);
            messageBox.Message.Problem = "Timelapse could not load the Template File as its name is too long:" + Environment.NewLine;
            messageBox.Message.Problem += "\u2022 " + databasePath;
            messageBox.Message.Reason = "Windows cannot perform file operations if the folder path combined with the file name is more than " + Constant.File.MaxPathLength.ToString() + " characters.";
            messageBox.Message.Solution = "\u2022 Shorten the path name by moving your image folder higher up the folder hierarchy, or" + Environment.NewLine + "\u2022 Use shorter folder or file names.";
            messageBox.Message.Hint = "Files created in your " + Constant.File.BackupFolder + " folder must also be less than " + Constant.File.MaxPathLength.ToString() + " characters.";
            messageBox.Message.Icon = MessageBoxImage.Error;
            messageBox.ShowDialog();
        }

        // Warn the user if backups may not be made
        public static void BackupPathTooLongDialog(Window owner)
        {
            MessageBox messageBox = new MessageBox("Timelapse may not be able to backup your files", owner);
            messageBox.Message.Problem = "Timelapse may not be able to backup your files as your file names are very long.";

            messageBox.Message.Reason = "Timelapse normally creates backups of your template, database, and csv files in the " + Constant.File.BackupFolder + " folder." + Environment.NewLine;
            messageBox.Message.Reason += "However, Windows cannot create those files if the " + Constant.File.BackupFolder + " folder path combined with the file name is more than " + Constant.File.MaxPathLength.ToString() + " characters.";

            messageBox.Message.Solution = "\u2022 Shorten the path name by moving your image folder higher up the folder hierarchy, or" + Environment.NewLine;
            messageBox.Message.Solution += "\u2022 Use shorter folder or file names.";
            messageBox.Message.Hint = "You can still use Timelapse, but backup files may not be created.";
            messageBox.Message.Icon = MessageBoxImage.Warning;
            messageBox.ShowDialog();
        }
        #endregion

        #region Dialog Message: Problem loading the template
        public static void TemplateFileNotLoadedAsCorrupt(string templateDatabasePath, Window owner)
        {
            Util.ThrowIf.IsNullArgument(owner, nameof(owner));
            // notify the user the template couldn't be loaded rather than silently doing nothing
            MessageBox messageBox = new MessageBox("Timelapse could not load the Template file.", owner);
            messageBox.Message.Problem = "Timelapse could not load the Template File :" + Environment.NewLine;
            messageBox.Message.Problem += "\u2022 " + templateDatabasePath;
            messageBox.Message.Reason = String.Format("The template ({0}) file may be corrupted, unreadable, or otherwise invalid.", Constant.File.TemplateDatabaseFileExtension);
            messageBox.Message.Solution = "Try one or more of the following:" + Environment.NewLine;
            messageBox.Message.Solution += "\u2022 recreate the template, or use another copy of it." + Environment.NewLine;
            messageBox.Message.Solution += String.Format("\u2022 check if there is a valid template file in your {0} folder.", Constant.File.BackupFolder) + Environment.NewLine;
            messageBox.Message.Solution += String.Format("\u2022 email {0} describing what happened, attaching a copy of your {1} file.", Constant.ExternalLinks.EmailAddress, Constant.File.TemplateDatabaseFileExtension);

            messageBox.Message.Result = "Timelapse did not affect any of your other files.";
            if (owner.Name.Equals("Timelapse"))
            {
                // Only displayed in Timelapse, not the template editor
                messageBox.Message.Hint = "See if you can open and examine the template file in the Timelapse Template Editor." + Environment.NewLine;
                messageBox.Message.Hint += "If you can't, and if you don't have a copy elsewhere, you will have to recreate it.";
            }
            messageBox.Message.Icon = MessageBoxImage.Error;
            messageBox.ShowDialog();
        }
        #endregion

        #region Dialog Messages: Corrupted .DDB file (no primary key)
        public static void DatabaseFileNotLoadedAsCorrupt(string ddbDatabasePath, bool isEmpty, Window owner)
        {
            // notify the user the database couldn't be loaded because there is a problem with it
            MessageBox messageBox = new MessageBox("Timelapse could not load your database file.", owner);
            messageBox.Message.Problem = "Timelapse could not load your .ddb database file:" + Environment.NewLine;
            messageBox.Message.Problem += "\u2022 " + ddbDatabasePath;
            if (isEmpty)
            {
                messageBox.Message.Reason = "Your database file is empty. Possible reasons include:" + Environment.NewLine;
            }
            else
            {
                messageBox.Message.Reason = "Your database is unreadable or corrupted. Possible reasons include:" + Environment.NewLine;
            }
            messageBox.Message.Reason += "\u2022 Timelapse was shut down (or crashed) in the midst of:" + Environment.NewLine;
            messageBox.Message.Reason += "    - loading your image set for the first time, or" + Environment.NewLine;
            messageBox.Message.Reason += "    - writing your data into the file, or" + Environment.NewLine;
            messageBox.Message.Reason += "\u2022 system, security or network  restrictions prohibited file reading and writing, or," + Environment.NewLine;
            messageBox.Message.Reason += "\u2022 some other unkown reason.";
            messageBox.Message.Solution = "\u2022 If you have not analyzed any images yet, delete the .ddb file and try again." + Environment.NewLine;
            messageBox.Message.Solution += "\u2022 Also, check for valid backups of your database in your " + Constant.File.BackupFolder + " folder that you can reuse.";
            messageBox.Message.Hint = "IMPORTANT: Send a copy of your .ddb and .tdb files along with an explanatory note to saul@ucalgary.ca." + Environment.NewLine;
            messageBox.Message.Hint += "He will check those files to see if there is a fixable bug.";
            messageBox.Message.Icon = MessageBoxImage.Error;
            messageBox.ShowDialog();
        }
        #endregion

        #region Dialog Message: Show Exception Reporting  
        // REPLACED BY ExceptionShutdownDialog  - DELETE after we are sure that other method works 
        /// <summary>
        /// Display a dialog showing unhandled exceptions. The dialog text is also placed in the clipboard so that the user can paste it into their email
        /// </summary>
        /// <param name="programName">The name of the program that generated the exception</param>
        /// <param name="e">the exception</param>
        /// <param name="owner">A window where the message will be positioned within it</param>
        //public static void ShowExceptionReportingDialog(string programName, UnhandledExceptionEventArgs e, Window owner)
        //{
        //    // Check the arguments for null 
        //    ThrowIf.IsNullArgument(e, nameof(e));

        //    // once .NET 4.5+ is used it's meaningful to also report the .NET release version
        //    // See https://msdn.microsoft.com/en-us/library/hh925568.aspx.
        //    string title = programName + " needs to close. Please report this error.";
        //    MessageBox exitNotification = new MessageBox(title, owner);
        //    exitNotification.Message.Icon = MessageBoxImage.Error;
        //    exitNotification.Message.Title = title;
        //    exitNotification.Message.Problem = programName + " encountered a problem, likely due to a bug. If you let us know, we will try and fix it. ";
        //    exitNotification.Message.What = "Please help us fix it! You should be able to paste the entire content of the Reason section below into an email to saul@ucalgary.ca , along with a description of what you were doing at the time.  To quickly copy the text, click on the 'Reason' details, hit ctrl+a to select all of it, ctrl+c to copy, and then email all that.";
        //    exitNotification.Message.Reason = String.Format("{0}, {1}, .NET runtime {2}{3}", typeof(TimelapseWindow).Assembly.GetName(), Environment.OSVersion, Environment.Version, Environment.NewLine);
        //    if (e.ExceptionObject != null)
        //    {
        //        exitNotification.Message.Reason += e.ExceptionObject.ToString();
        //    }
        //    exitNotification.Message.Result = String.Format("The data file is likely OK.  If it's not you can restore from the {0} folder.", Constant.File.BackupFolder);
        //    exitNotification.Message.Hint = "\u2022 If you do the same thing this'll probably happen again.  If so, that's helpful to know as well." + Environment.NewLine;

        //    // Modify text for custom exceptions
        //    Exception custom_excepton = (Exception)e.ExceptionObject;
        //    switch (custom_excepton.Message)
        //    {
        //        case Constant.ExceptionTypes.TemplateReadWriteException:
        //            exitNotification.Message.Problem =
        //                programName + "  could not read data from the template (.tdb) file. This could be because: " + Environment.NewLine +
        //                "\u2022 the .tdb file is corrupt, or" + Environment.NewLine +
        //                "\u2022 your system is somehow blocking Timelapse from manipulating that file (e.g., Citrix security will do that)" + Environment.NewLine +
        //                "If you let us know, we will try and fix it. ";
        //            break;
        //        default:
        //            exitNotification.Message.Problem = programName + " encountered a problem, likely due to a bug. If you let us know, we will try and fix it. ";
        //            break;
        //    }
        //    Clipboard.SetText(exitNotification.Message.Reason);
        //    exitNotification.ShowDialog();
        //}
        #endregion

        #region Dialog Message: No Updates Available
        public static void NoUpdatesAvailableDialog(Window owner, string applicationName, Version currentVersionNumber)
        {
            MessageBox messageBox = new MessageBox(String.Format("No updates to {0} are available.", applicationName), owner);
            messageBox.Message.Reason = String.Format("You a running the latest version of {0}, version: {1}", applicationName, currentVersionNumber);
            messageBox.Message.Icon = MessageBoxImage.Information;
            messageBox.ShowDialog();
        }
        #endregion
    }
}
