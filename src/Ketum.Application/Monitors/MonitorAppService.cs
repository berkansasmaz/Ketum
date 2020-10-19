using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ketum.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Caching;

namespace Ketum.Monitors
{
    [Authorize(KetumPermissions.Monitoring.Default)]
    public class MonitorAppService : KetumAppService, IMonitorAppService
    {
        private readonly MonitorManager _monitorManager;
        private readonly IMonitorRepository _monitorRepository;
        private readonly IDistributedCache<MonitorWithDetailsDto> _cache;


        public MonitorAppService(
            MonitorManager monitorManager,
            IMonitorRepository monitorRepository,
            IDistributedCache<MonitorWithDetailsDto> cache)
        {
            _monitorManager = monitorManager;
            _monitorRepository = monitorRepository;
            _cache = cache;
        }

        public async Task<PagedResultDto<MonitorDto>> GetListAsync(GetMonitorsRequestInput input)
        {
            var count = await _monitorRepository.GetCountByFilterAsync(CurrentUser.Id!.Value);

            var monitors = await _monitorRepository.GetListAsync(
                input.Sorting,
                input.SkipCount,
                input.MaxResultCount,
                CurrentUser.Id!.Value);

            return new PagedResultDto<MonitorDto>(
                count,
                ObjectMapper.Map<List<Monitor>, List<MonitorDto>>(monitors)
            );
        }

        public async Task<MonitorWithDetailsDto> GetAsync(Guid id)
        {
            var cacheItem = await _cache.GetOrAddAsync(
                id.ToString(),
                async () => await GetMonitorFromDatabaseAsync(id),
                () => new DistributedCacheEntryOptions
                {
                    AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(KetumConsts.MonitorWorkerPeriod.TotalMinutes)
                }
            );

            var cache = cacheItem;
            return cache;
        }

        [Authorize(KetumPermissions.Monitoring.Create)]
        public async Task CreateAsync(CreateMonitorDto input)
        {
            await _monitorManager.CreateAsync(
                GuidGenerator.Create(),
                GuidGenerator.Create(),
                CurrentUser.Id!.Value,
                input.Name,
                input.Url,
                input.Interval);
        }

        [Authorize(KetumPermissions.Monitoring.Update)]
        public async Task UpdateAsync(Guid id, UpdateMonitorDto input)
        {
            await _monitorManager.UpdateAsync(
                id,
                CurrentUser.Id!.Value,
                input.Name,
                input.Url);
        }

        [Authorize(KetumPermissions.Monitoring.Delete)]
        public async Task DeleteAsync(Guid id)
        {
            var monitor = await _monitorRepository.GetAsync(id);

            if (CurrentUser.Id!.Value != monitor.CreatorId)
            {
                return;
            }

            await _monitorRepository.DeleteAsync(id);
        }

        private async Task<MonitorWithDetailsDto> GetMonitorFromDatabaseAsync(Guid monitorId)
        {
            var monitor = await _monitorRepository.GetAsync(monitorId);

            if (monitor.CreatorId != CurrentUser.Id!.Value)
            {
                return null;
            }

            var dto = ObjectMapper.Map<Monitor, MonitorWithDetailsDto>(monitor);

            if (monitor.MonitorStep.Type == MonitorStepTypes.Request)
            {
                var week = DateTime.UtcNow.AddDays(-14);

                var monitorStepLogs = monitor.MonitorStep.MonitorStepLogs
                    .Where(x => x.StartDate >= week && x.EndDate != null)
                    .OrderByDescending(x => x.StartDate)
                    .Take(50)
                    .ToList();

                monitorStepLogs = monitorStepLogs.OrderBy(x => x.StartDate).ToList();

                if (monitorStepLogs.Any(x => x.Status == MonitorStepStatusTypes.Success))
                {
                    dto.LoadTime = monitorStepLogs
                        .Where(x => x.Status == MonitorStepStatusTypes.Success)
                        .Average(x => x.EndDate!.Value.Subtract(x.StartDate).TotalMilliseconds);
                }

                foreach (var stepLog in monitorStepLogs)
                {
                    if (stepLog.Status.IsIn(MonitorStepStatusTypes.Success, MonitorStepStatusTypes.Fail))
                    {
                        dto.MonitoredTime += TimeSpan.FromMinutes(dto.MonitorStep.Interval).Minutes;

                        if (stepLog.Status == MonitorStepStatusTypes.Success)
                        {
                            dto.LoadTimes.Add(stepLog.EndDate!.Value.Subtract(stepLog.StartDate).TotalMilliseconds);
                        }

                        if (stepLog.Status == MonitorStepStatusTypes.Fail)
                            dto.DownTime += TimeSpan.FromMinutes(dto.MonitorStep.Interval).Minutes;

                        var currentDowntimePercent = dto.DownTime / dto.MonitoredTime * 100;
                        var currentUptimePercent = 100 - currentDowntimePercent;

                        dto.UpTimes.Add(double.IsNaN(currentUptimePercent) ? 0 : currentUptimePercent);
                    }
                }

                dto.DownTimePercent = dto.DownTime / dto.MonitoredTime * 100;
                dto.UpTime = 100 - dto.DownTimePercent;
                dto.UpTime = dto.UpTime < 0 ? 0 : dto.UpTime;
            }

            if (double.IsNaN(dto.UpTime))
            {
                dto.UpTime = 0;
                dto.DownTimePercent = 0;
            }

            return dto;
        }
    }
}