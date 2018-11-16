﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Timelapse.Util
{
    // Methods to manipulate a datagrid. 
    public static class DataGridExtensions
    {
        // Sort the given data grid by the given column number in ascending order
        public static void SortByColumnAscending(this DataGrid dataGrid, int columnNumber)
        {
            // Clear current sort descriptions
            dataGrid.Items.SortDescriptions.Clear();

            // Add the new sort description
            DataGridColumn firstColumn = dataGrid.Columns[columnNumber];
            ListSortDirection sortDirection = ListSortDirection.Ascending;
            dataGrid.Items.SortDescriptions.Add(new SortDescription(firstColumn.SortMemberPath, sortDirection));

            // Apply sort
            foreach (DataGridColumn column in dataGrid.Columns)
            {
                column.SortDirection = null;
            }
            firstColumn.SortDirection = sortDirection;

            // Refresh items to display sort
            dataGrid.Items.Refresh();
        }

        #region Code to enable multiple selections. 
        // Select the rows with the given IDs, discover its rowIndexes, and then scroll the topmost row into view
        // This method is provided with a list of tuples, each containing
        // - a File ID, 
        // - a possible row index into the data table containing that File ID
        // We want to select (highlight) each row in the data table matching those IDs. 
        // Typically, the file record identified by the ID will be found in the datagrid row specified by RowIndex,
        // which will occur *unless* the the user has resorted the datagrid by clicking a column header 
        // For efficiency, we check each tuple to see if the ID provided matches the ID in the row specified by rowIndex. If so, 
        // we can quickly highlight those rows.  Otherwise we need to search the datagrid for each ID
        public static void SelectAndScrollIntoView(this DataGrid dataGrid, List<Tuple<long, int>> idRowIndexes)
        {
            // If there are no selections, just unselect everything
            if (idRowIndexes.Count.Equals(0))
            {
                dataGrid.UnselectAll();
                return;
            }
           
            int topmostRowIndex = int.MaxValue; // Keeps track of the topmost row index, as this is the one we will want to scroll too
            DataRowView currentRow;                   // The current row being examined
            List<int> rowIndexesToSelect = new List<int>(); 
            long currentID = 0;        
            int currentRowIndexThatMayContainID = 0;

            foreach (Tuple<long, int> idRowIndex in idRowIndexes)
            {
                currentID = (int)idRowIndex.Item1;
                currentRowIndexThatMayContainID = (int)idRowIndex.Item2;

                // Get the row indicated by rowIndex (after first checking that such a row exists)
                if (dataGrid.Items.Count < currentRowIndexThatMayContainID)
                {
                    // System.Diagnostics.Debug.Print("row index " + currentRowIndexThatMayContainID + " is not in array sized " + dataGrid.Items.Count);
                    return;
                }
                currentRow = dataGrid.Items[currentRowIndexThatMayContainID] as DataRowView;

                if ((long)currentRow.Row.ItemArray[0] == currentID)
                {
                    // The ID is in the row indicated by rowIndex. Add that rowIndex as one of the rows we should select
                    rowIndexesToSelect.Add(currentRowIndexThatMayContainID);
                    if (topmostRowIndex > currentRowIndexThatMayContainID)
                    {
                        topmostRowIndex = currentRowIndexThatMayContainID;
                    }
                }
                else
                {
                    // The ID is not in the row indicated by rowIndex. Search the datagrid for that ID, and then add it as one of the rows we should select
                    bool idFound = false;
                    for (int index = 0; index < dataGrid.Items.Count; index++)
                    {
                        currentRow = dataGrid.Items[index] as DataRowView;
                        if ((long)currentRow.Row.ItemArray[0] == currentID)
                        {
                            idFound = true;
                            rowIndexesToSelect.Add(index);
                            if (topmostRowIndex > index)
                            {
                                topmostRowIndex = index;
                            }
                            break;
                        }
                    }
                    if (idFound == false)
                    {
                        // The id should always be found. But just in case  we ignore IDS that aren't found
                        // System.Diagnostics.Debug.Print("could not find ID: " + currentID + " in array sized " + dataGrid.Items.Count);
                    }
                }
            }
            #if DeBUG
            // We don't expect these to fail, but just in case
            if (rowIndexesToSelect.Count != IdRowIndexes.Count || topmostRowIndex == int.MaxValue)
            {
                System.Diagnostics.Debug.Print("RowIndexes and ID counts don't match: " + rowIndexesToSelect.Count + ", " + IdRowIndexes.Count);
            }
            #endif

            // Select the items (which highlights those rows)
            bool indexIncreasing = topmostRowIndex > dataGrid.SelectedIndex;
            SelectRowByIndexes(dataGrid, rowIndexesToSelect);

            // Depending on our selection direction, we scroll to expose the previous or next 2 rows to ensure they are visible beyond the selected row);
            int scrollIndex = indexIncreasing ? Math.Min(topmostRowIndex + 3, dataGrid.Items.Count - 1) : Math.Max(topmostRowIndex - 3, 0);
            dataGrid.ScrollIntoView(dataGrid.Items[scrollIndex]);
        }

        // Select the rows indicated by the (perhaps multple) row indexes
        // Modified from https://blog.magnusmontin.net/2013/11/08/how-to-programmatically-select-and-focus-a-row-or-cell-in-a-datagrid-in-wpf/
        private static void SelectRowByIndexes(DataGrid dataGrid, List<int> rowIndexes)
        {
            if (!dataGrid.SelectionUnit.Equals(DataGridSelectionUnit.FullRow) || !dataGrid.SelectionMode.Equals(DataGridSelectionMode.Extended))
            {
                // This should never be triggered
                throw new ArgumentException("DataGrid issue: SelectionUnit must be FullRow, and  Selection Mode must be  Extended");
            }

            // Clear all selections
            dataGrid.SelectedItems.Clear();

            // If 0 items are selected, there is nothing more to do
            if (rowIndexes.Count.Equals(0) || rowIndexes.Count > dataGrid.Items.Count)
            {
                return;
            }

            // if there is only one item, we can just set it directly
            if (rowIndexes.Count.Equals(1))
            {
                int rowIndex = rowIndexes[0];
                if (rowIndex < 0 || rowIndex > (dataGrid.Items.Count - 1))
                {
                    // This shouldn't happen, but...
                    throw new ArgumentException(string.Format("{0} is an invalid row index.", rowIndex));
                }
                dataGrid.SelectedIndex = rowIndex;  // This used to be for single selection. 
                return;
            }
            // Multiple indexes are selected
            foreach (int rowIndex in rowIndexes)
            {
                if (rowIndex < 0 || rowIndex > (dataGrid.Items.Count - 1))
                {
                    // This shouldn't happen, but...
                    throw new ArgumentException(string.Format("{0} is an invalid row index.", rowIndex));
                }
                dataGrid.SelectedItems.Add(dataGrid.Items[rowIndex]);

                // SAULXXX DELETE THIS CODE?
                // I can't recall why I had this code in here, as I can't see why I need to focus on a particular cell.
                // The bad side effect of it is that, if the datagrid is visible (e.g., as a separate pane), it grabs the focus
                // away from other windows.
                // //object item = dataGrid.Items[rowIndex];
                // DataGridRow row = dataGrid.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DataGridRow;
                // if (row == null)
                // {
                //    // dataGrid.ScrollIntoView(item);  CHANGE ABOVE TO SCROLL THE FIRST ITEM
                //    row = dataGrid.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DataGridRow;
                // }
                // if (row != null)
                // {
                //    DataGridCell cell = GetCell(dataGrid, row, 0);
                //    if (cell != null)
                //    { 
                //       cell.Focus();
                //    }
                // }
            }
        }

        // UNUSED 
        // Get a cell from the DataGrid
        // private static DataGridCell GetCell(DataGrid dataGrid, DataGridRow rowContainer, int column)
        // {
        //    if (rowContainer != null)
        //    {
        //        DataGridCellsPresenter presenter = FindVisualChild<DataGridCellsPresenter>(rowContainer);
        //        if (presenter == null)
        //        {
        //            /* if the row has been virtualized away, call its ApplyTemplate() method 
        //             * to build its visual tree in order for the DataGridCellsPresenter
        //             * and the DataGridCells to be created */
        //            rowContainer.ApplyTemplate();
        //            presenter = FindVisualChild<DataGridCellsPresenter>(rowContainer);
        //        }
        //        if (presenter != null)
        //        {
        //            DataGridCell cell = presenter.ItemContainerGenerator.ContainerFromIndex(column) as DataGridCell;
        //            if (cell == null)
        //            {
        //                /* bring the column into view
        //                 * in case it has been virtualized away */
        //                dataGrid.ScrollIntoView(rowContainer, dataGrid.Columns[column]);
        //                cell = presenter.ItemContainerGenerator.ContainerFromIndex(column) as DataGridCell;
        //            }
        //            return cell;
        //        }
        //    }
        //    return null;
        // }

        // Enumerate the members of a visual tree, in order to programmatic access objects in the visual tree.
        private static T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child != null && child is T)
                { 
                    return (T)child;
                }
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                    { 
                        return childOfChild;
                    }
                }
            }
            return null;
        }
        #endregion
    }
}
