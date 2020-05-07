﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Timelapse.Database;
using Timelapse.Enums;
using Timelapse.EventArguments;
using Timelapse.Images;
using RowColumn = System.Drawing.Point;

namespace Timelapse.Controls
{
    // Thumbnail Grid Overview - including cancellable asynchronous loading of images.
    // A user can use the mouse wheel to not only zoom into an image, but also to zoom out into an overview that displays 
    // multiple thumbnails at the same time in a grid. There are multiple levels of overviews, 
    // each adding an additional row of images at smaller sizes up to a minimum size and a maximum number of rows.
    // The user can multi-select images, where any data entered will be applied to the selected images.  
    // However, selections are reset between navigations and zoom levels.

    // While not yet done, this could  be extended to  use infinite scroll, but that could introduce some issues  in how user selections are done, 
    // where mis-selections are possible as some images will be out of site.

    public partial class ThumbnailGrid : UserControl
    {
        #region Public properties

        // DataEntryControls needs to be set externally
        public DataEntryControls DataEntryControls { get; set; }

        // FileTable needs to be set externally
        public FileTable FileTable { set; get; }

        // FileTableStartIndex needs to be set externally
        public int FileTableStartIndex { get; set; }

        // FoldePath needs to be set externally
        // The root folder containing the template
        public string FolderPath { get; set; }

        // The number of images that currently exist in a row
        public int ImagesInRow
        {
            get
            {
                return this.Grid.ColumnDefinitions.Count;
            }
        }

        // The number of rows that currently exist in the ThumbnailGrid
        public int RowsInGrid
        {
            get
            {
                return this.Grid.RowDefinitions.Count;
            }
        }
        #endregion

        #region Private variables

        private List<ThumbnailInCell> thumbnailInCells;

        // Track states between mouse down / move and up 
        private RowColumn cellChosenOnMouseDown;
        private bool modifierKeyPressedOnMouseDown = false;
        private RowColumn cellWithLastMouseOver = new RowColumn(-1, -1);
        #endregion

        #region Constructor
        public ThumbnailGrid()
        {
            this.InitializeComponent();
            this.FileTableStartIndex = 0;
        }
        #endregion

        #region Public Refresh
        // Rebuild the grid, based on 
        // - fitting the image into as many cells of the same size that can fit within the grid
        // - retaining information about images previously shown on this grid, which importantly includes its selection status.
        //   this means users can do some selections, then change the zoom level.
        //   Note that every refresh unselects previously selected images
        public bool Refresh(double cellHeight, double gridWidth, double gridHeight)
        {
            // If nothing is loaded, or if there is no desiredWidth, then there is nothing to refresh
            if (this.FileTable == null || !this.FileTable.Any() || cellHeight == 0)
            {
                return false;
            }
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

            // Get the first image as a sample to determine the apect ration, which we will use the set the width of all columns. 
            // It may not be a representative aspect ratio of all images, but its a reasonably heuristic. 
            // Note that the choice for getting the aspect ratio this way is a bit complicated. We can't just get the 'imageToDisplay' as it may
            // not be the correct one if we are navigating on the thumbnailGrid, or if it happens to be a video. So the easiest - albeit slightly less efficient -
            // way to do it is to grab the aspect ratio of the first image that will be displayed in the Thumbnail Grid. If it doesn't exist, we just use a default aspect ratio
            // Another option - to avoid the cost of gettng a bitmap on a video - is to check if its a video (jut check the path suffix) and if so use the default aspect ratio OR
            // use FFMPEG Probe (but that may mean another dll?)
            BitmapSource bm = this.FileTable[FileTableStartIndex].LoadBitmap(this.FolderPath, Constant.ImageValues.PreviewWidth32, ImageDisplayIntentEnum.Ephemeral, ImageDimensionEnum.UseWidth, out _);
            double cellWidth = (bm == null || bm.PixelHeight == 0) ? cellHeight * Constant.ThumbnailGrid.AspectRatioDefault : cellHeight * bm.PixelWidth / bm.PixelHeight;

            // Reconstruct the Grid with the appropriate rows/columns 
            if (this.ReconstructGrid(cellWidth, cellHeight, gridWidth, gridHeight, FileTableStartIndex) == false)
            {
                // Abort as the grid cannot  display even a single image
                Mouse.OverrideCursor = null;
                return false;
            }

            // Unselect all cells except the first one
            this.SelectInitialCellOnly();
            Mouse.OverrideCursor = null;
            return true;
        }
        #endregion

        #region Mouse callbacks
        // Mouse left down. Select images
        // The selection behaviours depend upon whether the CTL or SHIFT modifier key is pressed, or whether this is a double click 
        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ThumbnailInCell ci;
            this.cellChosenOnMouseDown = this.GetCellFromPoint(Mouse.GetPosition(this.Grid));
            RowColumn currentCell = this.GetCellFromPoint(Mouse.GetPosition(this.Grid));
            this.cellWithLastMouseOver = currentCell;

            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                // CTL mouse down: change that cell (and only that cell's) state
                this.modifierKeyPressedOnMouseDown = true;
                if (Equals(this.cellChosenOnMouseDown, currentCell))
                {
                    ci = this.GetThumbnailInCellFromCell(currentCell);
                    if (ci != null)
                    {
                        ci.IsSelected = !ci.IsSelected;
                    }
                }
            }
            else if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                // SHIFT mouse down: extend the selection (if any) to this point.
                this.modifierKeyPressedOnMouseDown = true;
                this.SelectExtendSelectionFrom(currentCell);
            }
            else
            {
                // Left mouse down, no modifiers keys. 
                // Select only the current cell, unselecting others.
                ci = this.GetThumbnailInCellFromCell(currentCell);
                if (ci != null)
                {
                    this.SelectNone();
                    ci.IsSelected = true;
                }
            }

            // If this is a double click, raise the Double click event, e.g., so that the calling app can navigate to that image.
            if (e.ClickCount == 2)
            {
                ci = this.GetThumbnailInCellFromCell(currentCell);
                ThumbnailGridEventArgs eventArgs = new ThumbnailGridEventArgs(this, ci?.ImageRow);
                this.OnDoubleClick(eventArgs);
                e.Handled = true; // Stops the double click from generating a marker on the MarkableImageCanvas
            }
            this.EnableOrDisableControlsAsNeeded();
            ThumbnailGridEventArgs selectionEventArgs = new ThumbnailGridEventArgs(this, null);
            this.OnSelectionChanged(selectionEventArgs);
        }

        // If a mouse-left drag movement, select all cells between the starting and current cell
        private void Grid_MouseMove(object sender, MouseEventArgs e)
        {
            // We only pay attention to mouse-left moves without any modifier keys pressed (i.e., a drag action).
            if (e.LeftButton != MouseButtonState.Pressed || this.modifierKeyPressedOnMouseDown)
            {
                return;
            }

            // Get the cell under the mouse pointer
            RowColumn currentCell = this.GetCellFromPoint(Mouse.GetPosition(this.Grid));

            // Ignore if the cell has already been handled in the last mouse down or move event,
            if (Equals(currentCell, this.cellWithLastMouseOver))
            {
                return;
            }
            this.SelectFromInitialCellTo(currentCell);
            this.cellWithLastMouseOver = currentCell;
            this.EnableOrDisableControlsAsNeeded();
        }

        // On the mouse up, select all cells contained by its bounding box. Note that this is needed
        // as well as the mouse move version, as otherwise a down/up on the same spot won't select the cell.
        private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            this.cellWithLastMouseOver.X = -1;
            this.cellWithLastMouseOver.Y = -1;
            if (this.modifierKeyPressedOnMouseDown)
            {
                this.modifierKeyPressedOnMouseDown = false;
                return;
            }
        }
        #endregion

        #region Grid Selection 
        // Unselect all elements in the grid
        // Select the first (and only the first) image in the current grid
        public void SelectInitialCellOnly()
        {
            this.SelectNone(); // Clear the selections
            if (this.thumbnailInCells.Any())
            {
                ThumbnailInCell ci = this.thumbnailInCells[0];
                ci.IsSelected = true;
            }
            ThumbnailGridEventArgs eventArgs = new ThumbnailGridEventArgs(this, null);
            this.OnSelectionChanged(eventArgs);
        }

        private void SelectNone()
        {
            // Unselect all ThumbnailInCells
            foreach (ThumbnailInCell ci in this.thumbnailInCells)
            {
                ci.IsSelected = false;
            }
        }

        // Select all cells between the initial and currently selected cell
        private void SelectFromInitialCellTo(RowColumn currentCell)
        {
            // If the first selected cell doesn't exist, make it the same as the currently selected cell
            if (this.cellChosenOnMouseDown == null)
            {
                this.cellChosenOnMouseDown = currentCell;
            }
            this.SelectNone(); // Clear the selections

            // Determine which cell is 
            DetermineTopLeftBottomRightCells(this.cellChosenOnMouseDown, currentCell, out RowColumn startCell, out RowColumn endCell);

            // Select the cells defined by the cells running from the topLeft cell to the BottomRight cell
            RowColumn indexCell = startCell;

            ThumbnailInCell ci;
            while (true)
            {
                ci = this.GetThumbnailInCellFromCell(indexCell);
                // If the cell doesn't contain a ThumbnailInCell, then we are at the end.
                if (ci == null)
                {
                    break;
                }
                ci.IsSelected = true;

                // If there is no next cell, then we are at the end.
                if (this.GridGetNextCell(indexCell, endCell, out RowColumn nextCell) == false)
                {
                    break;
                }
                indexCell = nextCell;
            }
            ThumbnailGridEventArgs eventArgs = new ThumbnailGridEventArgs(this, null);
            this.OnSelectionChanged(eventArgs);
        }

        // Select all cells between the initial and currently selected cell
        private void SelectFromTo(RowColumn cell1, RowColumn cell2)
        {
            DetermineTopLeftBottomRightCells(cell1, cell2, out RowColumn startCell, out RowColumn endCell);

            // Select the cells defined by the cells running from the topLeft cell to the BottomRight cell
            RowColumn indexCell = startCell;

            ThumbnailInCell ci;
            while (true)
            {
                ci = this.GetThumbnailInCellFromCell(indexCell);
                // This shouldn't happen, but ensure that the cell contains a ThumbnailInCell.
                if (ci == null)
                {
                    break;
                }
                ci.IsSelected = true;

                // If there is no next cell, then we are at the end.
                if (this.GridGetNextCell(indexCell, endCell, out RowColumn nextCell) == false)
                {
                    break;
                }
                indexCell = nextCell;
            }
            ThumbnailGridEventArgs eventArgs = new ThumbnailGridEventArgs(this, null);
            this.OnSelectionChanged(eventArgs);
        }

        private void SelectExtendSelectionFrom(RowColumn currentCell)
        {
            // If there is no previous cell, then we are at the end.
            if (this.GridGetPreviousSelectedCell(currentCell, out RowColumn previousCell) == true)
            {
                this.SelectFromTo(previousCell, currentCell);
            }
            else if (this.GridGetNextSelectedCell(currentCell, out RowColumn nextCell) == true)
            {
                this.SelectFromTo(currentCell, nextCell);
            }
        }

        // Get the Selected times as a list of file table indexes to the current displayed selection of files (note these are not the IDs)
        public List<int> GetSelected()
        {
            List<int> selected = new List<int>();
            if (this.thumbnailInCells == null)
            {
                return selected;
            }
            foreach (ThumbnailInCell ci in this.thumbnailInCells)
            {
                if (ci.IsSelected)
                {
                    int fileIndex = ci.FileTableIndex;
                    selected.Add(fileIndex);
                }
            }
            return selected;
        }

        public int SelectedCount()
        {
            return this.GetSelected().Count;
        }
        #endregion

        private BackgroundWorker BackgroundWorker;

        #region Reconstruct the grid, including clearing it
        private bool ReconstructGrid(double cellWidth, double cellHeight, double gridWidth, double gridHeight, int fileTableStartIndex)
        {
            int fileTableIndex;
            this.BackgroundWorker = new BackgroundWorker()
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };

            this.BackgroundWorker.DoWork += (ow, ea) =>
            {
                try
                {
                    LoadImageProgressStatus lip;

                    // Pass 1: Render the images (as they render faster)
                    fileTableIndex = fileTableStartIndex;
                    foreach (ThumbnailInCell thumbnailInCell in thumbnailInCells)
                    {

                        if (thumbnailInCell.ImageRow.IsVideo == false)
                        {
                            if (this.BackgroundWorker.CancellationPending == true)
                            {
                                ea.Cancel = true;
                                return;
                            }

                            BitmapSource bm = thumbnailInCell.GetThumbnail(cellWidth, cellHeight);
                            lip = new LoadImageProgressStatus
                            {
                                ThumbnailInCell = thumbnailInCell,
                                BitmapSource = bm,
                                GridIndex = thumbnailInCell.GridIndex,
                                CellWidth = cellWidth,
                                FileTableIndex = fileTableIndex,
                            };
                            if (this.BackgroundWorker.CancellationPending == true)
                            {
                                ea.Cancel = true;
                                return;
                            }
                            this.BackgroundWorker.ReportProgress(0, lip);
                        }
                        fileTableIndex++;
                    }

                    // Pass 2: Then render the videos (as these are slower)
                    fileTableIndex = fileTableStartIndex;
                    foreach (ThumbnailInCell thumbnailInCell in thumbnailInCells)
                    {
                        if (thumbnailInCell.ImageRow.IsVideo)
                        {
                            if (this.BackgroundWorker.CancellationPending == true)
                            {
                                ea.Cancel = true;
                                return;
                            }
                            BitmapSource bm = thumbnailInCell.GetThumbnail(cellWidth, cellHeight);
                            lip = new LoadImageProgressStatus
                            {
                                ThumbnailInCell = thumbnailInCell,
                                BitmapSource = bm,
                                GridIndex = thumbnailInCell.GridIndex,
                                CellWidth = cellWidth,
                                FileTableIndex = fileTableIndex,
                            };
                            if (this.BackgroundWorker.CancellationPending == true)
                            {
                                ea.Cancel = true;
                                return;
                            }
                            this.BackgroundWorker.ReportProgress(0, lip);
                        }
                        fileTableIndex++;
                    }
                }
                catch
                {
                    // Uncomment to trace 
                    // System.Diagnostics.Debug.Print("DoWork Aborted");
                    return;
                }
            };

            this.BackgroundWorker.ProgressChanged += (o, ea) =>
            {
                // this gets called on the UI thread
                LoadImageProgressStatus lip = (LoadImageProgressStatus)ea.UserState;
                this.UpdateThumbnailsLoadProgress(lip);
            };

            this.BackgroundWorker.RunWorkerCompleted += (o, ea) =>
            {
                this.BackgroundWorker.Dispose();
                return;
            };

            this.CancelThumbnailUpdate();
            if (this.RebuildGrid(gridWidth, gridHeight, cellWidth, cellHeight, fileTableStartIndex) == false)
            {
                // We can't even fit a single cell into the grid, so abort
                return false;
            }
            this.BackgroundWorker.RunWorkerAsync();
            return true;
        }
        #endregion

        // Clear the grid, and recreate it with a thumbnailInCell user control in each cell
        private bool RebuildGrid(double gridWidth, double gridHeight, double cellWidth, double cellHeight, int fileTableIndex)
        {
            // Calculated the number of rows/columns that can fit into the available space,
            int rowCount = Convert.ToInt32(Math.Floor(gridHeight / cellHeight));
            int columnCount = Convert.ToInt32(Math.Floor(gridWidth / cellWidth));
            if (rowCount == 0 || columnCount == 0)
            {
                // We can't even fit a single row or column in, so no point in continuing.
                return false; // rowsColumns;
            }

            // Clear the Grid so we can start afresh
            this.Grid.RowDefinitions.Clear();
            this.Grid.ColumnDefinitions.Clear();
            this.Grid.Children.Clear();

            // Add as many columns of the and rows of the given cell width and height as can fit into the grid's available space
            for (int currentColumn = 0; currentColumn < columnCount; currentColumn++)
            {
                this.Grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition() { Width = new GridLength(cellWidth, GridUnitType.Pixel) });
            }
            for (int currentRow = 0; currentRow < rowCount; currentRow++)
            {
                this.Grid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(cellHeight, GridUnitType.Pixel) });
            }

            int gridIndex = 0;
            int fileTableCount = this.FileTable.RowCount;
            ThumbnailInCell emptyThumbnailInCell;
            this.thumbnailInCells = new List<ThumbnailInCell>();

            // Add an empty thumbnailInCell to each grid cell until no more cells or files to display. The bitmap will be added via the backgroundWorker.
            for (int currentRow = 0; currentRow < rowCount; currentRow++)
            {
                for (int currentColumn = 0; currentColumn < columnCount && fileTableIndex < fileTableCount; currentColumn++)
                {
                    emptyThumbnailInCell = CreateEmptyThumbnail(fileTableIndex++, gridIndex++, cellWidth, cellHeight, currentRow, currentColumn);
                    Grid.SetRow(emptyThumbnailInCell, currentRow);
                    Grid.SetColumn(emptyThumbnailInCell, currentColumn);
                    this.Grid.Children.Add(emptyThumbnailInCell);
                    this.thumbnailInCells.Add(emptyThumbnailInCell);
                }
            }
            return true;
        }
        #region Progress and Cancelling 
        private void UpdateThumbnailsLoadProgress(LoadImageProgressStatus lip)
        {
            try
            {
                // As we are cancelling updates rapidly, check to make sure that we can still access the variables
                if (lip.ThumbnailInCell.GridIndex < this.thumbnailInCells.Count && lip.BitmapSource != null)
                {
                    ThumbnailInCell thumbnailInCell = this.thumbnailInCells[lip.GridIndex];
                    thumbnailInCell.SetThumbnail(lip.BitmapSource);
                    thumbnailInCell.RefreshBoundingBoxesAndEpisodeInfo(this.FileTable, lip.FileTableIndex);
                }
                
                //else
                //{
                //    System.Diagnostics.Debug.Print(String.Format("UpdateThumbnailsLoadProgress Aborted | IndexoutOfRange={0}, BitmapSource is null={1}", lip.ThumbnailInCell.Position >= this.emptyThumbnailInCells.Count, lip.BitmapSource == null));
                //}
            }
            catch
            {
                // Uncomment for tracing purposes
                // System.Diagnostics.Debug.Print("UpdateThumbnailsLoadProgress Aborted | Catch");
            }
        }

        private void CancelThumbnailUpdate()
        {
            this.CancelUpdate();
        }
        public void CancelUpdate()
        {
            try
            {
                if (this.BackgroundWorker != null)
                {
                    this.BackgroundWorker.CancelAsync();
                }
            }
            catch { }
        }
        #endregion

        #region CreateThumbnail
        private ThumbnailInCell CreateEmptyThumbnail(int fileTableIndex, int gridIndex, double cellWidth, double cellHeight, int row, int column)
        {
            ThumbnailInCell thumbnailInCell = new ThumbnailInCell(cellWidth, cellHeight)
            {
                GridIndex = gridIndex,
                Row = row,
                Column = column,
                RootFolder = this.FolderPath,
                ImageRow = this.FileTable[fileTableIndex],
                FileTableIndex = fileTableIndex,
                BoundingBoxes = Util.GlobalReferences.MainWindow.GetBoundingBoxesForCurrentFile(this.FileTable[fileTableIndex].ID)
            };
            return thumbnailInCell;
        }
        #endregion

        public void DisplayEpisodeTextIfWarranted()
        {
            foreach (ThumbnailInCell thumbnailInCell in this.thumbnailInCells)
            {
                thumbnailInCell.RefreshEpisodeInfo(this.FileTable, thumbnailInCell.FileTableIndex);
            }
        }

        public void RefreshBoundingBoxesAndEpisodeInfo()
        {
            foreach (ThumbnailInCell thumbnailInCell in this.thumbnailInCells)
            {
                thumbnailInCell.RefreshBoundingBoxesAndEpisodeInfo(this.FileTable, thumbnailInCell.FileTableIndex);
            }
        }
        #region Cell Navigation methods
        private bool GridGetNextSelectedCell(RowColumn cell, out RowColumn nextCell)
        {
            RowColumn lastCell = new RowColumn(this.Grid.RowDefinitions.Count - 1, this.Grid.ColumnDefinitions.Count - 1);
            ThumbnailInCell ci;

            while (this.GridGetNextCell(cell, lastCell, out nextCell))
            {
                ci = this.GetThumbnailInCellFromCell(nextCell);

                // If there is no cell, we've reached the end, 
                if (ci == null)
                {
                    return false;
                }
                // We've found a selected cell
                if (ci.IsSelected)
                {
                    return true;
                }
                cell = nextCell;
            }
            return false;
        }

        private bool GridGetPreviousSelectedCell(RowColumn cell, out RowColumn previousCell)
        {
            RowColumn lastCell = new RowColumn(0, 0);
            ThumbnailInCell ci;

            while (this.GridGetPreviousCell(cell, lastCell, out previousCell))
            {
                ci = this.GetThumbnailInCellFromCell(previousCell);

                // If there is no cell, terminate as we've reached the beginning
                if (ci == null)
                {
                    return false;
                }
                // We've found a selected cell
                if (ci.IsSelected)
                {
                    return true;
                }
                cell = previousCell;
            }
            return false;
        }
        // Get the next cell and return true
        // Return false if we hit the lastCell or the end of the grid.
        private bool GridGetNextCell(RowColumn cell, RowColumn lastCell, out RowColumn nextCell)
        {
            nextCell = new RowColumn(cell.X, cell.Y);
            // Try to go to the next column or wrap around to the next row if we are at the end of the row
            nextCell.Y++;
            if (nextCell.Y == this.Grid.ColumnDefinitions.Count)
            {
                // start a new row
                nextCell.Y = 0;
                nextCell.X++;
            }

            if (nextCell.X > lastCell.X || (nextCell.X == lastCell.X && nextCell.Y > lastCell.Y))
            {
                // We just went beyond the last cell, so we've reached the end.
                return false;
            }
            return true;
        }

        // Get the previous cell. Return true if we can, otherwise false.
        private bool GridGetPreviousCell(RowColumn cell, RowColumn firstCell, out RowColumn previousCell)
        {
            previousCell = new RowColumn(cell.X, cell.Y);
            // Try to go to the previous column or wrap around to the previous row if we are at the beginning of the row
            previousCell.Y--;
            if (previousCell.Y < 0)
            {
                // go to the previous row
                previousCell.Y = this.Grid.ColumnDefinitions.Count - 1;
                previousCell.X--;
            }

            if (previousCell.X < firstCell.X || (previousCell.X == firstCell.X && previousCell.Y < firstCell.Y))
            {
                // We just went beyond the last cell, so we've reached the end.
                return false;
            }
            return true;
        }
        #endregion

        #region Cell Calculation methods
        // Given two cells, determine which one is the start vs the end cell
        private static void DetermineTopLeftBottomRightCells(RowColumn cell1, RowColumn cell2, out RowColumn startCell, out RowColumn endCell)
        {
            startCell = (cell1.X < cell2.X || (cell1.X == cell2.X && cell1.Y <= cell2.Y)) ? cell1 : cell2;
            endCell = Equals(startCell, cell1) ? cell2 : cell1;
        }

        // Given a mouse point, return a point that indicates the (row, column) of the grid that the mouse point is over
        private RowColumn GetCellFromPoint(Point mousePoint)
        {
            RowColumn cell = new RowColumn(0, 0);
            double accumulatedHeight = 0.0;
            double accumulatedWidth = 0.0;

            // Calculate which row the mouse was over
            foreach (var rowDefinition in this.Grid.RowDefinitions)
            {
                accumulatedHeight += rowDefinition.ActualHeight;
                if (accumulatedHeight >= mousePoint.Y)
                {
                    break;
                }
                cell.X++;
            }

            // Calculate which column the mouse was over
            foreach (var columnDefinition in this.Grid.ColumnDefinitions)
            {
                accumulatedWidth += columnDefinition.ActualWidth;
                if (accumulatedWidth >= mousePoint.X)
                {
                    break;
                }
                cell.Y++;
            }
            return cell;
        }

        // Get the ThumbnailInCell held by the Grid's specified row,column coordinates 
        private ThumbnailInCell GetThumbnailInCellFromCell(RowColumn cell)
        {
            return this.Grid.Children.Cast<ThumbnailInCell>().FirstOrDefault(exp => Grid.GetColumn(exp) == cell.Y && Grid.GetRow(exp) == cell.X);
        }
        #endregion

        #region Enabling controls
        // Update the data entry controls to match the current selection(s)
        private void EnableOrDisableControlsAsNeeded()
        {
            if (this.Visibility == Visibility.Collapsed)
            {
                this.DataEntryControls.SetEnableState(ControlsEnableStateEnum.SingleImageView, -1);
            }
            else
            {
                this.DataEntryControls.SetEnableState(ControlsEnableStateEnum.MultipleImageView, this.SelectedCount());
            }
        }
        #endregion

        #region Events
        public event EventHandler<ThumbnailGridEventArgs> DoubleClick;
        public event EventHandler<ThumbnailGridEventArgs> SelectionChanged;

        protected virtual void OnDoubleClick(ThumbnailGridEventArgs e)
        {
            this.DoubleClick?.Invoke(this, e);
        }

        protected virtual void OnSelectionChanged(ThumbnailGridEventArgs e)
        {
            this.SelectionChanged?.Invoke(this, e);
        }
        #endregion
    }

    #region Class: LoadImageProgressStatus
    // Used by ReportProgress to pass specific values to Progress Changed as a parameter 
    internal class LoadImageProgressStatus
    {
        public ThumbnailInCell ThumbnailInCell { get; set; } = null;
        public BitmapSource BitmapSource { get; set; } = null;
        public int GridIndex { get; set; } = 0;
        public double CellWidth { get; set; } = 0;
        public int FileTableIndex { get; set; }
        public LoadImageProgressStatus() { }
    }
    #endregion
}