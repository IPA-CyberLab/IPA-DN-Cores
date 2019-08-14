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
        public IActionResult Status([FromRoute] string id)
        {
            App app = Server.AppGet(id);

            SingleData<App> data = new SingleData<App>(id, app, ModelMode.Edit);

            return View(data);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
