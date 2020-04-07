﻿using Microsoft.Win32;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MessageBox = Timelapse.Dialog.MessageBox;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;

namespace Timelapse.Util
{
    /// <summary>
    /// A variety of miscellaneous utility functions
    /// </summary>
    public static class Utilities
    {
        // Get the visual child of the specified type
        // Invoke by, e.g., TextBlock tb = Utilities.GetVisualChild<TextBlock>(somePartentUIElement);
        // Code from: http://techiethings.blogspot.com/2010/05/get-wpf-datagrid-row-and-cell.html
        public static T GetVisualChild<T>(Visual parent) where T : Visual
        {
            T child = default;
            int numVisuals = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < numVisuals; i++)
            {
                Visual v = (Visual)VisualTreeHelper.GetChild(parent, i);
                child = v as T;
                if (child == null)
                {
                    child = GetVisualChild<T>(v);
                }
                if (child != null)
                {
                    break;
                }
            }
            return child;
        }

        // Similar to the above, except it also considers the name of the child.
        // Get the visual child of the specified type with the matching name
        // Invoke by, e.g., TextBlock tb = Utilities.GetVisualChild<TextBlock>(somePartentUIElement, name);
        public static T GetVisualChild<T>(DependencyObject parent, string childName)
           where T : DependencyObject
        {
            // Confirm parent and childName are valid. 
            if (parent == null) return null;

            T foundChild = null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                // If the child is not of the request child type child
                if (!(child is T))
                {
                    // recursively drill down the tree
                    foundChild = GetVisualChild<T>(child, childName);

                    // If the child is found, break so we do not overwrite the found child. 
                    if (foundChild != null) break;
                }
                else if (!string.IsNullOrEmpty(childName))
                {
                    // If the child's name is set for search
                    if (child is FrameworkElement frameworkElement && frameworkElement.Name == childName)
                    {
                        // if the child's name is of the request name
                        foundChild = (T)child;
                        break;
                    }
                }
                else
                {
                    // child element found.
                    foundChild = (T)child;
                    break;
                }
            }
            return foundChild;
        }



        // Check to see if the language is english (en) and culture is en-US or en-CA. Return those values as well
        public static bool CheckAndGetLangaugeAndCulture(out string language, out string culturename, out string displayname)
        {
            System.Globalization.CultureInfo cultureInfo = System.Threading.Thread.CurrentThread.CurrentCulture;
            language = cultureInfo.TwoLetterISOLanguageName;
            culturename = cultureInfo.Name;
            displayname = cultureInfo.DisplayName;
            return language == "en" && (culturename == "en-US" || culturename == "en-CA");
        }

        // This isn't used yet, but we could use it when we switch to .Net 4.5 or higher
        public static string GetDotNetVersion()
        {
            // adapted from https://msdn.microsoft.com/en-us/library/hh925568.aspx.
            int release = 0;
            using (RegistryKey tempRegKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            {
                using (RegistryKey ndpKey = tempRegKey.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\"))
                {
                    if (ndpKey != null)
                    {
                        object releaseAsObject = ndpKey.GetValue("Release");
                        if (releaseAsObject != null)
                        {
                            release = (int)releaseAsObject;
                        }
                    }

                    if (release >= 394802)
                    {
                        return "4.6.2 or later";
                    }
                    if (release >= 394254)
                    {
                        return "4.6.1";
                    }
                    if (release >= 393295)
                    {
                        return "4.6";
                    }
                    if (release >= 379893)
                    {
                        return "4.5.2";
                    }
                    if (release >= 378675)
                    {
                        return "4.5.1";
                    }
                    if (release >= 378389)
                    {
                        return "4.5";
                    }
                    return "4.5 or later not detected";
                }
            }
        }

        public static ParallelOptions GetParallelOptions(int maximumDegreeOfParallelism)
        {
            ParallelOptions parallelOptions = new ParallelOptions()
            {
                MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, maximumDegreeOfParallelism)
            };
            return parallelOptions;
        }

        public static bool IsSingleTemplateFileDrag(DragEventArgs dragEvent, out string templateDatabasePath)
        {
            // Check the arguments for null 
            if (dragEvent != null && dragEvent.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedFiles = (string[])dragEvent.Data.GetData(DataFormats.FileDrop);
                if (droppedFiles != null && droppedFiles.Length == 1)
                {
                    templateDatabasePath = droppedFiles[0];
                    if (Path.GetExtension(templateDatabasePath) == Constant.File.TemplateDatabaseFileExtension)
                    {
                        return true;
                    }
                }
            }

            templateDatabasePath = null;
            return false;
        }

        public static void OnHelpDocumentPreviewDrag(DragEventArgs dragEvent)
        {
            // Check the arguments for null 
            ThrowIf.IsNullArgument(dragEvent, nameof(dragEvent));
            if (Utilities.IsSingleTemplateFileDrag(dragEvent, out _))
            {
                dragEvent.Effects = DragDropEffects.All;
            }
            else
            {
                dragEvent.Effects = DragDropEffects.None;
            }
            dragEvent.Handled = true;
        }

        public static void ShowExceptionReportingDialog(string programName, UnhandledExceptionEventArgs e, Window owner)
        {
            // Check the arguments for null 
            ThrowIf.IsNullArgument(e, nameof(e));

            // once .NET 4.5+ is used it's meaningful to also report the .NET release version
            // See https://msdn.microsoft.com/en-us/library/hh925568.aspx.
            string title = programName + " needs to close. Please report this error.";
            MessageBox exitNotification = new MessageBox(title, owner);
            exitNotification.Message.Icon = MessageBoxImage.Error;
            exitNotification.Message.Title = title;
            exitNotification.Message.Problem = programName + " encountered a problem, likely due to a bug. If you let us know, we will try and fix it. ";
            exitNotification.Message.What = "Please help us fix it! You should be able to paste the entire content of the Reason section below into an email to saul@ucalgary.ca , along with a description of what you were doing at the time.  To quickly copy the text, click on the 'Reason' details, hit ctrl+a to select all of it, ctrl+c to copy, and then email all that.";
            exitNotification.Message.Reason = String.Format("{0}, {1}, .NET runtime {2}{3}", typeof(TimelapseWindow).Assembly.GetName(), Environment.OSVersion, Environment.Version, Environment.NewLine);
            if (e.ExceptionObject != null)
            {
                exitNotification.Message.Reason += e.ExceptionObject.ToString();
            }
            exitNotification.Message.Result = String.Format("The data file is likely OK.  If it's not you can restore from the {0} folder.", Constant.File.BackupFolder);
            exitNotification.Message.Hint = "\u2022 If you do the same thing this'll probably happen again.  If so, that's helpful to know as well." + Environment.NewLine;

            // Modify text for custom exceptions
            Exception custom_excepton = (Exception)e.ExceptionObject;
            switch (custom_excepton.Message)
            {
                case Constant.ExceptionTypes.TemplateReadWriteException:
                    exitNotification.Message.Problem =
                        programName + "  could not read data from the template (.tdb) file. This could be because: " + Environment.NewLine +
                        "\u2022 the .tdb file is corrupt, or" + Environment.NewLine +
                        "\u2022 your system is somehow blocking Timelapse from manipulating that file (e.g., Citrix security will do that)" + Environment.NewLine +
                        "If you let us know, we will try and fix it. ";
                    break;
                default:
                    exitNotification.Message.Problem = programName + " encountered a problem, likely due to a bug. If you let us know, we will try and fix it. ";
                    break;
            }

            Clipboard.SetText(exitNotification.Message.Reason);
            exitNotification.ShowDialog();
        }

        // get a location for the template database from the user
        public static bool TryGetFileFromUser(string title, string defaultFilePath, string filter, string defaultExtension, out string selectedFilePath)
        {
            // Get the template file, which should be located where the images reside
            using (OpenFileDialog openFileDialog = new OpenFileDialog()
            {
                Title = title,
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                AutoUpgradeEnabled = true,

                // Set filter for file extension and default file extension 
                DefaultExt = defaultExtension,
                Filter = filter
            })
            {
                if (String.IsNullOrWhiteSpace(defaultFilePath))
                {
                    openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
                else
                {
                    openFileDialog.InitialDirectory = Path.GetDirectoryName(defaultFilePath);
                    openFileDialog.FileName = Path.GetFileName(defaultFilePath);
                }

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    selectedFilePath = openFileDialog.FileName;
                    return true;
                }

                selectedFilePath = null;
                return false;
            }
        }

        /// <summary>
        /// Format the passed value for use as string value in a SQL statement or query.
        /// </summary>
        public static string QuoteForSql(string value)
        {
            // promote null values to empty strings
            if (value == null)
            {
                return "''";
            }

            // for an input of "foo's bar" the output is "'foo''s bar'"
            return "'" + value.Replace("'", "''") + "'";
        }
    }
}
