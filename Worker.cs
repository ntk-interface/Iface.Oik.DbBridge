using System.Threading;
using System.Threading.Tasks;
using Iface.Oik.Tm.Helpers;
using Iface.Oik.Tm.Interfaces;
using Microsoft.Extensions.Hosting;

namespace OikTask
{
    public class Worker : BackgroundService
    {
        private const int WorkerDelay = 1000;

        private readonly ICommonInfrastructure _infr;
        private readonly IOikDataApi _api;


        public Worker(ICommonInfrastructure infr,
                      IOikDataApi api)
        {
            _infr = infr;
            _api = api;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await DoWork();
                await Task.Delay(WorkerDelay, stoppingToken);
            }
        }


        public async Task DoWork()
        {
            Tms.PrintDebug(_infr.TmUserInfo?.Name);
            Tms.PrintDebug(await _api.GetSystemTimeString());

            var ts = new TmStatus(20, 1, 1);
            var ti = new TmAnalog(20, 1, 1);

            await _api.UpdateTagPropertiesAndClassData(ts);
            await _api.UpdateStatus(ts);

            await _api.UpdateTagPropertiesAndClassData(ti);
            await _api.UpdateAnalog(ti);

            Tms.PrintDebug(ts);
            Tms.PrintDebug(ti);
        }
    }
}