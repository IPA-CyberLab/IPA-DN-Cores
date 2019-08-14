using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DaemonCenter.Models;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;

using IPA.Cores.Basic.App.DaemonCenterLib;

namespace DaemonCenter.Controllers
{
    public class AppController : Controller
    {
        readonly Server Server;

        public AppController(Server server)
        {
            this.Server = server;
        }

        [Authorize]
        public IActionResult _new()
        {
            return View();
        }

        // App 一覧
        public IActionResult Index()
        {
            IReadOnlyList<KeyValuePair<string, App>> appList = Server.AppEnum();

            return View(appList);
        }

        // App ステータスページ (インスタンス一覧の表示)
        public IActionResult Status([FromRoute] string id, [FromQuery] int mode)
        {
            App app = Server.AppGet(id);

            IEnumerable<Instance> instanceList = app.InstanceList;

            // ソート
            instanceList = instanceList.OrderBy(x => x.HostName);

            // フィルタの実施
            if (mode == 1) instanceList = instanceList.Where(x => x.IsActive(app.Settings, DateTimeOffset.Now));

            if (mode == 2) instanceList = instanceList.Where(x => !x.IsActive(app.Settings, DateTimeOffset.Now));

            DualData<App, List<Instance>> data = new DualData<App, List<Instance>>(id, app, id, instanceList.ToList(), ModelMode.Edit);

            return View(data);
        }

        // インスタンス情報
        public IActionResult InstanceInfo([FromRoute] string id, [FromRoute] string id2)
        {
            App app = Server.AppGet(id);

            Instance inst = app.GetInstanceById(id2);

            DualData<App, Instance> data = new DualData<App, Instance>(id, app, id2, inst, ModelMode.Edit);

            return View(data);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
