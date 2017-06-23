﻿using System.Collections.Generic;
using System.Windows;
using Timelapse.Util;

namespace Timelapse.Dialog
{
    /// <summary>
    /// Interaction logic for DeleteDuplicates.xaml
    /// </summary>
    public partial class DeleteDuplicates : Window
    {
        private List<string> fileNames;
        public DeleteDuplicates(Window owner, List<string> filenames)
        {
            this.InitializeComponent();
            this.Owner = owner;
            this.fileNames = filenames;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Utilities.SetDefaultDialogPosition(this);
            Utilities.TryFitWindowInWorkingArea(this);
            if (this.fileNames.Count == 0)
            {
                this.DeletedFiles.Items.Add("No duplicate files found.");
                this.OkButton.IsEnabled = false;
            }
            else
            {
                this.OkButton.IsEnabled = true;
                this.DeletedFiles.ItemsSource = this.fileNames;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}