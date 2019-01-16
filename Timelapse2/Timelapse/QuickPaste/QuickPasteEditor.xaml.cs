﻿using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Timelapse.Controls;
using Timelapse.Database;
using Timelapse.Dialog;

namespace Timelapse.QuickPaste
{
    // Given a QuickPasteEntry (a name and a list of QuickPasteItems),
    // allow the user to edit it.
    // Currently, the only thing that is editable is its name and whether a particular item's data should be included when pasted
    public partial class QuickPasteEditor : Window
    {
        // Columns where fields will be placed in the grid
        private const int GridColumnUse = 1;
        private const int GridColumnLabel = 2;
        private const int GridColumnValue = 3;

        // The initial grid row. We start adding rows after this one.
        // the 1st two grid rows are already filled.
        private const int GridRowInitialRow = 1;

        // UI Constants
        private const double ValuesWidth = 80;
        private const double ValuesHeight = 22;

        public QuickPasteEntry QuickPasteEntry { get; set; }
        private FileDatabase fileDatabase;

        public QuickPasteEditor(QuickPasteEntry quickPasteEntry, FileDatabase fileDatabase)
        {
            InitializeComponent();
            this.QuickPasteEntry = quickPasteEntry;
            this.fileDatabase = fileDatabase;
        }

        // When the window is loaded
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Adjust this dialog window position 
            Dialogs.SetDefaultDialogPosition(this);
            Dialogs.TryFitDialogWindowInWorkingArea(this);

            // Display the title of the QuickPasteEntry
            this.QuickPasteTitle.Text = this.QuickPasteEntry.Title;
            this.QuickPasteTitle.TextChanged += QuickPasteTitle_TextChanged;

            // Build the grid rows, each displaying successive items in the QuickPasteItems list
            BuildRows();
        }

        // Build a row displaying each QuickPaste item
        private void BuildRows()
        {
            // We start after the GridRowInitialRow
            int gridRowIndex = GridRowInitialRow;

            foreach (QuickPasteItem quickPasteItem in this.QuickPasteEntry.Items)
            {
                ++gridRowIndex;
                RowDefinition gridRow = new RowDefinition()
                {
                     Height = GridLength.Auto
                };
                this.QuickPasteGridRows.RowDefinitions.Add(gridRow);
                BuildRow(quickPasteItem, gridRow, gridRowIndex);
            }
        }

        // Given a quickPasteItem (essential the information representing a single data control and its value),
        // - add a row to the grid with controls that display that information,
        // - add a checkbox that can be selected to indicate whether that information should be included in a paste operation
        private void BuildRow(QuickPasteItem quickPasteItem, RowDefinition gridRow, int gridRowIndex)
        {
            // USE Column: A checkbox to indicate whether the current search row should be used as part of the search
            Thickness thickness = new Thickness(0, 2, 0, 2);
            CheckBox useCurrentRow = new CheckBox()
            {
                Margin = thickness,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                IsChecked = quickPasteItem.Use,
                Tag = quickPasteItem
            };
            useCurrentRow.Checked += UseCurrentRow_CheckChanged;
            useCurrentRow.Unchecked += UseCurrentRow_CheckChanged;

            Grid.SetRow(useCurrentRow, gridRowIndex);
            Grid.SetColumn(useCurrentRow, GridColumnUse);
            this.QuickPasteGridRows.Children.Add(useCurrentRow);

            // LABEL column: The label associated with the control (Note: not the data label)
            TextBlock controlLabel = new TextBlock()
            {
                Margin = new Thickness(5),
                Text = quickPasteItem.Label,
                Foreground = quickPasteItem.Use ? Brushes.Black : Brushes.Gray,
            };
            Grid.SetRow(controlLabel, gridRowIndex);
            Grid.SetColumn(controlLabel, GridColumnLabel);
            this.QuickPasteGridRows.Children.Add(controlLabel);

            // Value column: The value is presented as an editable field particular to its control type
            if (quickPasteItem.ControlType == Constant.Control.Note ||
                quickPasteItem.ControlType == Constant.Control.Counter)
            {
                // Notes and Counters both uses a text field, so they can be constructed as a textbox
                AutocompleteTextBox textBoxValue = new AutocompleteTextBox()
                {
                    Autocompletions = this.fileDatabase.GetDistinctValuesInFileDataColumn(quickPasteItem.DataLabel),
                    Text = quickPasteItem.Value,
                    Height = ValuesHeight,
                    Width = ValuesWidth,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    IsEnabled = quickPasteItem.Use,
                    Tag = quickPasteItem
                };

                // Counter text fields are modified to only allow numeric input
                if (quickPasteItem.ControlType == Constant.Control.Counter)
                {
                    textBoxValue.PreviewTextInput += this.Counter_PreviewTextInput;
                    DataObject.AddPastingHandler(textBoxValue, this.Counter_Paste);
                }
                textBoxValue.TextChanged += this.NoteOrCounter_TextChanged;

                Grid.SetRow(textBoxValue, gridRowIndex);
                Grid.SetColumn(textBoxValue, GridColumnValue);
                this.QuickPasteGridRows.Children.Add(textBoxValue);
            }
            else if (quickPasteItem.ControlType == Constant.Control.FixedChoice)
            {
                // Choices use choiceboxes
                ControlRow controlRow = this.fileDatabase.GetControlFromTemplateTable(quickPasteItem.DataLabel);
                ComboBox comboBoxValue = new ComboBox()
                {
                    Height = ValuesHeight,
                    Width = ValuesWidth,
                    // Create the dropdown menu 
                    ItemsSource = controlRow.GetChoicesForQuickPasteMenu(),
                    SelectedItem = quickPasteItem.Value,
                    IsEnabled = quickPasteItem.Use,
                    Tag = quickPasteItem
                };
                Grid.SetRow(comboBoxValue, gridRowIndex);
                Grid.SetColumn(comboBoxValue, GridColumnValue);
                this.QuickPasteGridRows.Children.Add(comboBoxValue);
                comboBoxValue.SelectionChanged += this.FixedChoice_SelectionChanged;
            }
            else if (quickPasteItem.ControlType == Constant.Control.Flag)
            {
                // Flags present checkable checkboxes
                CheckBox flagCheckBox = new CheckBox()
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    IsChecked = String.Equals(quickPasteItem.Value, Constant.BooleanValue.False, StringComparison.OrdinalIgnoreCase) ? false : true,
                    IsEnabled = quickPasteItem.Use,
                    Tag = quickPasteItem
                };
                flagCheckBox.Checked += this.Flag_CheckedOrUnchecked;
                flagCheckBox.Unchecked += this.Flag_CheckedOrUnchecked;

                Grid.SetRow(flagCheckBox, gridRowIndex);
                Grid.SetColumn(flagCheckBox, GridColumnValue);
                this.QuickPasteGridRows.Children.Add(flagCheckBox);
            }
            else
            {
                // We should never get here
                throw new NotSupportedException(String.Format("Unhandled control type in QuickPasteEditor '{0}'.", quickPasteItem.ControlType));
            }
        }

        #region UI Callbacks
        // Update the QuickPasteEntry's title
        private void QuickPasteTitle_TextChanged(object sender, TextChangedEventArgs e)
        {
            this.QuickPasteEntry.Title = this.QuickPasteTitle.Text;
        }
        
        // Invoke when the user clicks the checkbox to enable or disable the data row
        private void UseCurrentRow_CheckChanged(object sender, RoutedEventArgs e)
        {
            CheckBox cbox = sender as CheckBox;

            // Enable or disable the controls on that row to reflect whether the checkbox is checked or unchecked
            int row = Grid.GetRow(cbox);

            TextBlock label = this.GetGridElement<TextBlock>(GridColumnLabel, row);
            UIElement value = this.GetGridElement<UIElement>(GridColumnValue, row);
            label.Foreground = cbox.IsChecked == true ? Brushes.Black : Brushes.Gray;
            value.IsEnabled = cbox.IsChecked.Value;

            // Update the QuickPaste row data structure to reflect the current checkbox state
            QuickPasteItem quickPasteRow = (QuickPasteItem)cbox.Tag;
            quickPasteRow.Use = cbox.IsChecked == true;
        }

        // Value (Counters and Notes): The user has selected a new value
        // - set its corresponding value in the quickPasteItem data structure
        // - update the UI to show the new value
        private void NoteOrCounter_TextChanged(object sender, TextChangedEventArgs args)
        {
            TextBox textBox = sender as TextBox;
            QuickPasteItem quickPasteItem = (QuickPasteItem)textBox.Tag;
            quickPasteItem.Value = textBox.Text;
        }

        // Value (Counter) Ensure the textbox accept only typed numbers 
        private void Counter_PreviewTextInput(object sender, TextCompositionEventArgs args)
        {
            args.Handled = IsNumbersOnly(args.Text);
        }

        // Value (FixedChoice): The user has selected a new value 
        // - set its corresponding value in the quickPasteItem data structure
        // - update the UI to show the new value
        private void FixedChoice_SelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            ComboBox comboBox = sender as ComboBox;
            QuickPasteItem quickPasteItem = (QuickPasteItem)comboBox.Tag;
            quickPasteItem.Value = comboBox.SelectedValue.ToString();
        }

        // Value (Flags): The user has checked or unchecked a new value 
        // - set its corresponding value in the quickPasteItem data structure
        // - update the UI to show the new value
        private void Flag_CheckedOrUnchecked(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = sender as CheckBox;
            QuickPasteItem quickPasteItem = (QuickPasteItem)checkBox.Tag;
            quickPasteItem.Value = checkBox.IsChecked.ToString();
        }
        #endregion

        #region Helper functions
        // CANDIDATE FOR UTILITIES MAYBE SAME AS UTIL.ISDIGIT? SIMILAR FUCNTION IN CUSTOMSELECTION.CS
        // Value(Counter) Helper function: checks if the text contains only numbers
        private static bool IsNumbersOnly(string text)
        {
            Regex regex = new Regex("[^0-9.-]+"); // regex that matches allowed text
            return regex.IsMatch(text);
        }

        // CANDIDATE FOR UTILITIES
        // Value (Counter) Helper function:  textbox accept only pasted numbers 
        private void Counter_Paste(object sender, DataObjectPastingEventArgs args)
        {
            bool isText = args.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true);
            if (!isText)
            {
                args.CancelCommand();
            }

            string text = args.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
            if (IsNumbersOnly(text))
            {
                args.CancelCommand();
            }
        }
        // Get the corresponding grid element from a given column, row, 
        private TElement GetGridElement<TElement>(int column, int row) where TElement : UIElement
        {
            return (TElement)this.QuickPasteGridRows.Children.Cast<UIElement>().First(control => Grid.GetRow(control) == row && Grid.GetColumn(control) == column);
        }
        #endregion

        #region Ok buttons
        // Apply the selection if the Ok button is clicked
        private void OkButton_Click(object sender, RoutedEventArgs args)
        {
            this.DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs args)
        {
            this.DialogResult = false;
        }
        #endregion
    }
}