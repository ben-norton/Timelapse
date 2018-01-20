﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Timelapse.Util;

namespace Timelapse.Editor.Dialog
{
    /// <summary>
    /// This dialog displays a list of metadata found in a selected file. 
    /// </summary>
    // Note: There are lots of commonalities between this dialog and DialogPopulate, but its not clear if it's worth the effort of factoring the two.
    public partial class InspectMetadata : Window
    {
        private Dictionary<string, Metadata> metadataDictionary;
        private string metadataName = String.Empty;
        private string noteLabel = String.Empty;
        private string noteDataLabel = String.Empty;

        private string imageFilePath;
        private string folderPath = String.Empty;

        public InspectMetadata(Window owner)
        {
            this.InitializeComponent();
            this.Owner = owner;
        }

        // After the interface is loaded, try to adjust the position of the dialog box
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Utilities.SetDefaultDialogPosition(this);
            Utilities.TryFitDialogWindowInWorkingArea(this);
        }

        #region Datagrid callbacks
        // Configuring the data grid appearance and select the first row
        private void Datagrid_AutoGeneratedColumns(object sender, EventArgs e)
        {
            this.dataGrid.Columns[0].Header = "Key";
            this.dataGrid.Columns[1].Header = "Metadata kind";
            this.dataGrid.Columns[2].Header = "Metadata name";
            this.dataGrid.Columns[3].Header = "Example value from current file";
            this.dataGrid.SortByColumnAscending(2);
            this.dataGrid.Columns[0].Visibility = Visibility.Collapsed;
            this.dataGrid.Columns[1].Width = 130;

            // Select the first row
            if (this.dataGrid.Items.Count > 0)
            {
                this.dataGrid.SelectedIndex = 0;
                this.dataGrid.Focus();
            }
        }

        // The user has selected a row. Get the metadata from that row, and display the metadata name.
        private void Datagrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            IList<DataGridCellInfo> selectedcells = e.AddedCells;

            // Make sure there are actually some selected cells
            if (selectedcells == null || selectedcells.Count == 0)
            {
                return;
            }

            // We should only have a single selected cell, so just grab the first one
            DataGridCellInfo di = selectedcells[0];

            // the selected item is the entire row, where the format returned is [MetadataName , MetadataValue] 
            // Parse out the metadata name and display it
            String[] s = di.Item.ToString().Split(',');  // Get the "[Metadataname" portion before the ','

            this.metadataName = s[0].Substring(1);       // Remove the leading '[' or '('
            if (this.metadataDictionary.ContainsKey(this.metadataName))
            {
                this.MetadataDisplayText.Text = this.metadataDictionary[this.metadataName].Name;
                this.MetadataDisplayText.ToolTip = this.MetadataDisplayText.Text;
            }
            else
            {
                this.MetadataDisplayText.Text = String.Empty;
                this.MetadataDisplayText.ToolTip = String.Empty;
            }
        }
        #endregion

        #region UI Button Callbacks
        // When the user opens the file, get its metadata and display it in the datagrid
        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            string filter = String.Format("Images and videos (*{0};*{1};*{2})|*{0};*{1};*{2}", Constant.File.JpgFileExtension, Constant.File.AviFileExtension, Constant.File.Mp4FileExtension);
            if (Utilities.TryGetFileFromUser("Select a typical file to inspect", ".", filter, out this.imageFilePath) == true)
            {
                this.ImageName.Content = Path.GetFileName(this.imageFilePath);
                this.ImageName.ToolTip = this.ImageName.Content;

                // Retrieve the metadata
                this.metadataDictionary = MetadataDictionary.LoadMetadata(this.imageFilePath);
                // If there is no metadata, this is an easy way to inform the user
                if (this.metadataDictionary.Count == 0)
                {
                    this.metadataDictionary.Add("Empty", new Timelapse.Util.Metadata("Empty", "No metadata found", String.Empty));
                }

                // In order to populate the datagrid, we have to unpack the dictionary as a list containing four values
                List<Tuple<string, string, string, string>> metadataList = new List<Tuple<string, string, string, string>>();
                foreach (KeyValuePair<string, Metadata> metadata in this.metadataDictionary)
                {
                    metadataList.Add(new Tuple<string, string, string, string>(metadata.Key, metadata.Value.Directory, metadata.Value.Name, metadata.Value.Value));
                }
                this.dataGrid.ItemsSource = metadataList;
            }
        }

        private void OkayButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
        #endregion
    }
}
