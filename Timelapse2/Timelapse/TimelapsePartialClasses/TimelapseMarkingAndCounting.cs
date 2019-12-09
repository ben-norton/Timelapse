﻿using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Timelapse.Controls;
using Timelapse.EventArguments;
using Timelapse.Images;

namespace Timelapse
{
    // Marking and Counting
    public partial class TimelapseWindow : Window, IDisposable
    {
        // Event handler: A marker, as defined in e.Marker, has been either added (if e.IsNew is true) or deleted (if it is false)
        // Depending on which it is, add or delete the tag from the current counter control's list of tags 
        // If its deleted, remove the tag from the current counter control's list of tags
        // Every addition / deletion requires us to:
        // - update the contents of the counter control 
        // - update the data held by the image
        // - update the list of markers held by that counter
        // - regenerate the list of markers used by the markableCanvas
        private void MarkableCanvas_RaiseMarkerEvent(object sender, MarkerEventArgs e)
        {
            if (e.IsNew)
            {
                // A marker has been added
                DataEntryCounter currentCounter = this.FindSelectedCounter(); // No counters are selected, so don't mark anything
                if (currentCounter == null)
                {
                    return;
                }
                this.MarkableCanvas_AddMarker(currentCounter, e.Marker);
                return;
            }
            // An existing marker has been deleted.
            DataEntryCounter counter = (DataEntryCounter)this.DataEntryControls.ControlsByDataLabel[e.Marker.DataLabel];

            // Part 1. Decrement the counter only if there is a number in it
            string oldCounterData = counter.Content;
            string newCounterData = String.Empty;
            if (!String.IsNullOrEmpty(oldCounterData))
            {
                int count = Convert.ToInt32(oldCounterData);
                count = (count == 0) ? 0 : count - 1;           // Make sure its never negative, which could happen if a person manually enters the count 
                newCounterData = count.ToString();

                if (!newCounterData.Equals(oldCounterData))
                {
                    // Don't bother updating if the value hasn't changed (i.e., already at a 0 count)
                    // Update the datatable and database with the new counter values
                    this.dataHandler.IsProgrammaticControlUpdate = true;
                    counter.SetContentAndTooltip(newCounterData);
                    this.dataHandler.IsProgrammaticControlUpdate = false;
                    this.dataHandler.FileDatabase.UpdateFile(this.dataHandler.ImageCache.Current.ID, counter.DataLabel, newCounterData);
                }
            }

            // Part 2. Remove the marker in memory and from the database
            // Each marker in the countercoords list reperesents a different control. 
            // So just check the first markers's DataLabel in each markersForCounters list to see if it matches the counter's datalabel.
            MarkersForCounter markersForCounter = null;
            foreach (MarkersForCounter markers in this.markersOnCurrentFile)
            {
                // If there are no markers, we don't have to do anything.
                if (markers.Markers.Count == 0)
                {
                    continue;
                }

                // There are no markers associated with this counter
                // if (markers.Markers[0].DataLabel == markers.DataLabel)
                if (markers.Markers[0].DataLabel == e.Marker.DataLabel)
                {
                    // We found the marker counter associated with that control
                    markersForCounter = markers;
                    break;
                }
            }

            // Part 3. Remove the found metatag from the metatagcounter and from the database
            if (markersForCounter != null)
            {
                markersForCounter.RemoveMarker(e.Marker);
                this.Speak(counter.Content); // Speak the current count
                this.dataHandler.FileDatabase.SetMarkerPositions(this.dataHandler.ImageCache.Current.ID, markersForCounter);
            }
            this.MarkableCanvas_UpdateMarkers(); // Refresh the Markable Canvas, where it will also delete the markers at the same time
        }

        /// <summary>
        /// A new marker associated with a counter control has been created;
        /// Increment the counter controls value, and add the marker to all data structures (including the database)
        /// </summary>
        private void MarkableCanvas_AddMarker(DataEntryCounter counter, Marker marker)
        {
            if (counter == null || marker == null)
            {
                // This shouldn't happen, but a user reported a 'null' crash somewhere in this method, so just in case...
                System.Diagnostics.Debug.Print("In MarkableCanvas_AddMarker. Counter or marker is null (and it shouldn't be");
                return;
            }

            // Get the Counter Control's contents,  increment its value (as we have added a new marker) 
            // Then update the control's content as well as the database
            // If we can't convert it to an int, assume that someone set the default value to either a non-integer in the template, or that it's a space. In either case, revert it to zero.
            // If we can't convert it to an int, assume that someone set the default value to either a non-integer in the template, or that it's a space. In either case, revert it to zero.
            if (Int32.TryParse(counter.Content, out int count) == false)
            {
                count = 0;
            }
            ++count;

            string counterContent = count.ToString();
            this.dataHandler.IsProgrammaticControlUpdate = true;
            this.dataHandler.FileDatabase.UpdateFile(this.dataHandler.ImageCache.Current.ID, counter.DataLabel, counterContent);
            counter.SetContentAndTooltip(counterContent);
            this.dataHandler.IsProgrammaticControlUpdate = false;

            // Find the MarkersForCounters associated with this particular control so we can add a marker to it
            MarkersForCounter markersForCounter = null;

            // PERFORMANCE: This was a quick hack to insert markers into the MarkersTable if it didn't already exist.
            // It mildely sucks as it means we have to rebuild in memory the entire markers table every time we add a new counter (if there is no row in it)
            // Need to revisit this later and do it far more efficiently.
            if (this.markersOnCurrentFile.Count == 0)
            {
                // Check - If there is no row in the marker table with that ID, an empty row (with null values) will be added to the database
                // and the Markers list held by the database will be updated accordingly
                if (this.dataHandler.FileDatabase.TryAddNewMarkerRow(this.dataHandler.ImageCache.Current.ID))
                {
                    // We added a new marker row, so we need to update the various markers data structures to reflect the new marker
                    markersForCounter = new MarkersForCounter(counter.DataLabel);
                    this.markersOnCurrentFile = this.dataHandler.FileDatabase.GetMarkersForCurrentFile(this.dataHandler.ImageCache.Current.ID);
                }
            }

            foreach (MarkersForCounter markers in this.markersOnCurrentFile)
            {
                if (markers.DataLabel == counter.DataLabel)
                {
                    markersForCounter = markers;
                    break;
                }
            }

            // fill in marker information
            marker.ShowLabel = true; // Show the annotation as its created. We will clear it on the next refresh
            marker.LabelShownPreviously = false;
            marker.Brush = Brushes.Red;               // Make it Red (for now)
            marker.DataLabel = counter.DataLabel;
            marker.Tooltip = counter.Label;   // The tooltip will be the counter label plus its data label
            marker.Tooltip += "\n" + counter.DataLabel;
            markersForCounter.AddMarker(marker);

            // update this counter's list of points in the database
            this.dataHandler.FileDatabase.SetMarkerPositions(this.dataHandler.ImageCache.Current.ID, markersForCounter);
            this.MarkableCanvas.Markers = this.GetDisplayMarkers();
            this.Speak(counter.Content + " " + counter.Label); // Speak the current count
        }

        // Create a list of markers from those stored in each image's counters, 
        // and then set the markableCanvas's list of markers to that list. We also reset the emphasis for those tags as needed.
        private void MarkableCanvas_UpdateMarkers()
        {
            this.MarkableCanvas.Markers = this.GetDisplayMarkers(); // By default, we don't show the annotation
        }

        private List<Marker> GetDisplayMarkers()
        {
            // No markers?
            if (this.markersOnCurrentFile == null)
            {
                return null;
            }

            // The markable canvas uses a simple list of markers to decide what to do.
            // So we just create that list here, where we also reset the emphasis of some of the markers
            List<Marker> markers = new List<Marker>();
            DataEntryCounter selectedCounter = this.FindSelectedCounter();
            for (int counter = 0; counter < this.markersOnCurrentFile.Count; counter++)
            {
                MarkersForCounter markersForCounter = this.markersOnCurrentFile[counter];
                if (this.DataEntryControls.ControlsByDataLabel.TryGetValue(markersForCounter.DataLabel, out DataEntryControl control) == false)
                {
                    // If we can't find the counter, its likely because the control was made invisible in the template,
                    // which means that there is no control associated with the marker. So just don't create the 
                    // markers associated with this control. Note that if the control is later made visible in the template,
                    // the markers will then be shown. 
                    continue;
                }

                // Update the emphasise for each tag to reflect how the user is interacting with tags
                DataEntryCounter currentCounter = (DataEntryCounter)this.DataEntryControls.ControlsByDataLabel[markersForCounter.DataLabel];
                bool emphasize = markersForCounter.DataLabel == this.State.MouseOverCounter;
                foreach (Marker marker in markersForCounter.Markers)
                {
                    // the first time through, show an annotation. Otherwise we clear the flags to hide the annotation.
                    if (marker.ShowLabel && !marker.LabelShownPreviously)
                    {
                        marker.ShowLabel = true;
                        marker.LabelShownPreviously = true;
                    }
                    else
                    {
                        marker.ShowLabel = false;
                    }

                    if (selectedCounter != null && currentCounter.DataLabel == selectedCounter.DataLabel)
                    {
                        marker.Brush = (SolidColorBrush)new BrushConverter().ConvertFromString(Constant.Defaults.SelectionColour);
                    }
                    else
                    {
                        marker.Brush = (SolidColorBrush)new BrushConverter().ConvertFromString(Constant.Defaults.StandardColour);
                    }

                    marker.Emphasise = emphasize;
                    marker.Tooltip = currentCounter.Label;
                    markers.Add(marker); // Add the MetaTag in the list 
                }
            }
            return markers;
        }
    }
}
