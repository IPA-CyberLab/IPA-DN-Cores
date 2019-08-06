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

using IPA.Cores.AspNet;
using IPA.Cores.Helper.AspNet;
using static IPA.Cores.Globals.AspNet;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;

using IPA.Cores.Basic.App.DaemonCenterLib;

namespace DaemonCenter.Controllers
{
    public class AppSettingsController : Controller
    {
        readonly Server Server;

        public AppSettingsController(Server server)
        {
            this.Server = server;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Add()
        {
            SingleData<AppSettings> model = new SingleData<AppSettings>();
            model.Mode = ModelMode.Add;

            return View("Edit", model);
        }

        [HttpPost]
        public IActionResult Add(SingleData<AppSettings> model)
        {
            model.Mode = ModelMode.Add;

            if (ModelState.IsValid == false)
            {
                return View("Edit", model);
            }

            string appId = Server.AppAdd(model.Data);

            return RedirectToAction(nameof(Index));
        }

        [Authorize]
        public IActionResult _new()
        {
            return View();
        }
    }
}
