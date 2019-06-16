﻿using System;
using System.Collections.Generic;

#pragma warning disable IDE1006 // Naming Style - we are using lower case names to match the json structure, we  mute the warning
namespace Timelapse.Detection
{
    // The Detector class holds data produced by Microsoft's Megadetector
    // Property names and structures follow the Microsoft Megadetetor JSON attribut names
    // in order to allow the JSON data to be deserialized into the Detector data structure
    public class Detector
    {
        public info info { get; set; }
        public Dictionary<string, string> detection_categories { get; set; }
        public Dictionary<string, string> classification_categories { get; set; }

        public List<image> images { get; set; }

        public Detector()
        {
            this.info = new info();
            this.images = new List<image>();
        }

        // Defaults are just used when reading in current csv files, as that file does not include the category definitions
        public void SetDetectionCategoryDefaults()
        {
            this.detection_categories = new Dictionary<string, string>
            {
                { "0", "empty" },
                { "1", "animal" },
                { "2", "person" },
                { "3", "group" },
                { "4", "vehicle" }
            };
        }

        // Defaults are just used when reading in current csv files, as that file does not include the category definitions
        public void SetDetectionClassificationDefaults()
        {
            this.classification_categories = new Dictionary<string, string>
            {
                { "1", "elk" },
                { "2", "wolf" },
                { "3", "bear" },
                { "6", "moose" }
            };
        }
    }

    public class info
    {
        public string detector { get; set; }
        public string detection_completion_time { get; set; }
        public string classifier { get; set; }
        public string classification_completion_time { get; set; }

        public info()
        {
        }

        // Defaults are just used when reading in current csv files, as that file does not include the detector information
        public void SetInfoDefaults()
        {
            this.detector = "megadetector_unknown_version";
            this.detection_completion_time = "unknown";
            this.classifier = "ecosystem1_unknown_version";
            this.classification_completion_time = "unknown";
        }
    }
    // TODO Replace file with a foreign key that points to the actual data table (or perhaps the foreign key should be in the actual data table, with just an index here.)

    public class image
    {
        public int imageID { get; set; }
        public string file { get; set; }
        public float max_detection_conf { get; set; }
        public List<detection> detections { get; set; }
        public image()
        {
            this.file = String.Empty;
            this.max_detection_conf = 0;
            this.detections = new List<detection>();
        }
    }

    // TODO Include a foreign key that points to the image table field ()
    public class detection
    {
        public int detectionID { get; set; }
        public string category { get; set; }
        public float conf { get; set; }
        public double[] bbox { get; set; }
        public List<Object[]> classifications { get; set; }
        public detection()
        {
            this.category = String.Empty;
            this.conf = 0;
            this.classifications = new List<Object[]>();
            this.bbox = new double[4];
        }
    }
}
