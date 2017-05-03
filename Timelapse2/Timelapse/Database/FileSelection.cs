﻿namespace Timelapse.Database
{
    public enum FileSelection : int
    {
        // file selections also used as image qualities
        Ok = 0,
        Dark = 1,
        Missing = 2,
        Corrupted = 3,

        // file selections only
        All = 4,
        MarkedForDeletion = 5,
        Custom = 6
    }
}
