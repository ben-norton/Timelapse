﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media;

namespace Timelapse.Images
{
    public class BoundingBoxes
    {
        // List of Bounding Boxes associated with the image
        public List<BoundingBox> Boxes { get; private set; }
        public float MaxConfidence { get; set; }
        public BoundingBoxes()
        {
            this.Boxes = new List<BoundingBox>();
            this.MaxConfidence = 0;
        }
    }
}