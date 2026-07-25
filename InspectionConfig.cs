using System.Collections.Generic;

namespace ccd_helper
{
    public class InspectionConfig
    {
        public List<ToolGroup> Tools { get; set; } = new();
        public List<Step> Steps { get; set; } = new();
        public List<string> ErrorReason { get; set; } = new();
    }

    public class ToolGroup
    {
        public List<string> ToolsImage { get; set; } = new();
    }

    public class Step
    {
        public int StepId { get; set; }
        public List<string> StepImages { get; set; } = new();
        public List<string> BoardImages { get; set; } = new();
    }
}