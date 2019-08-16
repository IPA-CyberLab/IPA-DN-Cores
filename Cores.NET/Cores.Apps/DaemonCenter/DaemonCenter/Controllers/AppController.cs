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
    [AutoValidateAntiforgeryToken]
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
        [HttpGet]
        public IActionResult Status([FromRoute] string id, [FromQuery] int mode)
        {
            App app = Server.AppGet(id);

            IEnumerable<Instance> instanceList = app.InstanceList;

            DualData<App, List<Instance>> data = GetFilteredInstanceListView(id, mode, app, instanceList);

            ViewBag.mode = mode;

            return View(data);
        }

        // App ステータスページにおける操作の実行と結果の応答
        [HttpPost]
        public IActionResult Status([FromRoute] string id, [FromQuery] int mode, [FromForm] IEnumerable<string> cb, [FromForm] OperationType operation, [FromForm] string args)
        {
            try
            {
                Server.AppInstanceOperation(id, cb, operation, args);
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }

            App app = Server.AppGet(id);

            IEnumerable<Instance> instanceList = app.InstanceList;

            // チェックボックスがチェックされたインスタンスの IsSelected フラグを true にする
            instanceList._DoForEach(inst => inst.ViewIsSelected = cb.Where(selectedId => (IgnoreCaseTrim)selectedId == inst.GetId(app)).Any());

            // 列挙結果を返す
            DualData<App, List<Instance>> data = GetFilteredInstanceListView(id, mode, app, instanceList);

            ViewBag.mode = mode;

            return View(data);
        }

        // インスタンス一覧データをフィルタリングして返す
        DualData<App, List<Instance>> GetFilteredInstanceListView(string appId, int filterMode, App app, IEnumerable<Instance> instanceList)
        {
            // ソート
            instanceList = instanceList.OrderBy(x => x.HostName);

            // フィルタの実施
            if (filterMode == 1) instanceList = instanceList.Where(x => x.IsActive(app.Settings, DateTimeOffset.Now));

            if (filterMode == 2) instanceList = instanceList.Where(x => !x.IsActive(app.Settings, DateTimeOffset.Now));

            DualData<App, List<Instance>> data = new DualData<App, List<Instance>>(appId, app, appId, instanceList.ToList(), ModelMode.Edit);

            return data;
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
