using System.ComponentModel.DataAnnotations;

namespace Ketum.Monitors
{
    public class UpdateMonitorDto
    {
        [Required]
        [StringLength(MonitorConsts.MaxNameLength)]
        public string Name { get; set; }

        [Required]
        [StringLength(MonitorStepConsts.MaxUrLength)]
        public string Url { get; set; }

        [Timestamp]
        [Range(MonitorStepConsts.MinIntervalValue, MonitorStepConsts.MaxIntervalValue)]
        public int Interval { get; set; }
    }
}