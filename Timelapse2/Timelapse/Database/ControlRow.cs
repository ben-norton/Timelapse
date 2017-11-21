﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Timelapse.Database
{
    public class ControlRow : DataRowBackedObject
    {
        private static readonly char[] BarDelimiter = { '|' };

        public ControlRow(DataRow row)
            : base(row)
        {
        }

        public long ControlOrder
        {
            get { return this.Row.GetLongField(Constant.Control.ControlOrder); }
            set { this.Row.SetField(Constant.Control.ControlOrder, value); }
        }

        public bool Copyable
        {
            get { return this.Row.GetBooleanField(Constant.Control.Copyable); }
            set { this.Row.SetField(Constant.Control.Copyable, value); }
        }

        public string DataLabel
        {
            get { return this.Row.GetStringField(Constant.Control.DataLabel); }
            set { this.Row.SetField(Constant.Control.DataLabel, value); }
        }

        public string DefaultValue
        {
            get { return this.Row.GetStringField(Constant.Control.DefaultValue); }
            set { this.Row.SetField(Constant.Control.DefaultValue, value); }
        }

        public string Label
        {
            get { return this.Row.GetStringField(Constant.Control.Label); }
            set { this.Row.SetField(Constant.Control.Label, value); }
        }

        public string List
        {
            get { return this.Row.GetStringField(Constant.Control.List); }
            set { this.Row.SetField(Constant.Control.List, value); }
        }

        public long SpreadsheetOrder
        {
            get { return this.Row.GetLongField(Constant.Control.SpreadsheetOrder); }
            set { this.Row.SetField(Constant.Control.SpreadsheetOrder, value); }
        }

        public int Width
        {
            get { return this.Row.GetIntegerField(Constant.Control.TextBoxWidth); }
            set { this.Row.SetField(Constant.Control.TextBoxWidth, value); }
        }

        public string Tooltip
        {
            get { return this.Row.GetStringField(Constant.Control.Tooltip); }
            set { this.Row.SetField(Constant.Control.Tooltip, value); }
        }

        public string Type
        {
            get { return this.Row.GetStringField(Constant.Control.Type); }
            set { this.Row.SetField(Constant.Control.Type, value); }
        }

        public bool Visible
        {
            get { return this.Row.GetBooleanField(Constant.Control.Visible); }
            set { this.Row.SetField(Constant.Control.Visible, value); }
        }

        // Parce and return the choice string into a list of items. 
        // Overload: the caller is uninterested in knowing if there are any empty items in the list, and wants the empty item removed
        public List<string> GetChoices(bool removeEmptyChoiceItem)
        {
            return this.GetChoices(out bool includesEmptyChoice, removeEmptyChoiceItem);
        }
        // Overload: the caller is interested in knowing if there are any empty items in the list, 
        // and wants the empty item removed (usually because they will add it themselves to a menue

        public List<string> GetChoices(out bool includesEmptyChoice)
        {
            bool removeEmptyChoiceItem = true;
            return this.GetChoices(out includesEmptyChoice, removeEmptyChoiceItem);
        }

        // Parce the choice string into a list of items. 
        // If it includes an empty choice item, set the includesEmptyChoice flag. 
        // Delete the empty choice item if the removeEmptyChoice flag is set
        public List<string> GetChoices(out bool includesEmptyChoice, bool removeEmptyChoiceItem)
        {
            List<string> list = this.List.Split(ControlRow.BarDelimiter).ToList();
            if (list.Contains(Constant.ControlMiscellaneous.EmptyChoiceItem))
            {
                includesEmptyChoice = true;
                if (removeEmptyChoiceItem)
                {
                    list.Remove(Constant.ControlMiscellaneous.EmptyChoiceItem);
                }
            }
            else
            {
                includesEmptyChoice = false;
            }
            return list;
        }

        public override ColumnTuplesWithWhere GetColumnTuples()
        {
            List<ColumnTuple> columnTuples = new List<ColumnTuple>
            {
                new ColumnTuple(Constant.Control.ControlOrder, this.ControlOrder),
                new ColumnTuple(Constant.Control.Copyable, this.Copyable),
                new ColumnTuple(Constant.Control.DataLabel, this.DataLabel),
                new ColumnTuple(Constant.Control.DefaultValue, this.DefaultValue),
                new ColumnTuple(Constant.Control.Label, this.Label),
                new ColumnTuple(Constant.Control.List, this.List),
                new ColumnTuple(Constant.Control.SpreadsheetOrder, this.SpreadsheetOrder),
                new ColumnTuple(Constant.Control.TextBoxWidth, this.Width),
                new ColumnTuple(Constant.Control.Tooltip, this.Tooltip),
                new ColumnTuple(Constant.Control.Type, this.Type),
                new ColumnTuple(Constant.Control.Visible, this.Visible)
            };
            return new ColumnTuplesWithWhere(columnTuples, this.ID);
        }

        public void SetChoices(List<string> choices)
        {
            this.List = String.Join("|", choices);
        }
        public bool Synchronize(ControlRow other)
        {
            bool synchronizationMadeChanges = false;
            if (this.Copyable != other.Copyable)
            {
                this.Copyable = other.Copyable;
                synchronizationMadeChanges = true;
            }
            if (this.ControlOrder != other.ControlOrder)
            {
                this.ControlOrder = other.ControlOrder;
                synchronizationMadeChanges = true;
            }
            if (this.DefaultValue != other.DefaultValue)
            {
                this.DefaultValue = other.DefaultValue;
                synchronizationMadeChanges = true;
            }
            if (this.Label != other.Label)
            {
                this.Label = other.Label;
                synchronizationMadeChanges = true;
            }
            if (this.List != other.List)
            {
                this.List = other.List;
                synchronizationMadeChanges = true;
            }
            if (this.SpreadsheetOrder != other.SpreadsheetOrder)
            {
                this.SpreadsheetOrder = other.SpreadsheetOrder;
                synchronizationMadeChanges = true;
            }
            if (this.Tooltip != other.Tooltip)
            {
                this.Tooltip = other.Tooltip;
                synchronizationMadeChanges = true;
            }
            if (this.Visible != other.Visible)
            {
                this.Visible = other.Visible;
                synchronizationMadeChanges = true;
            }
            if (this.Width != other.Width)
            {
                this.Width = other.Width;
                synchronizationMadeChanges = true;
            }

            return synchronizationMadeChanges;
        }
    }
}
