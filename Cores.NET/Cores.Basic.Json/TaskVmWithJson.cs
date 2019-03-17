using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Text;
using System.ComponentModel;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Reflection;
using System.Drawing;
using System.Runtime.InteropServices;

using IPA.Cores.Helper.Basic;
using System.Runtime.CompilerServices;

namespace IPA.Cores.Basic
{
    static partial class TaskUtil
    {

        public static object ConvertTask(object src_task_object, Type old_task_type, Type new_task_type)
        {
            Type src_task_def = typeof(Task<>).MakeGenericType(old_task_type);

            var cont_with_methods = src_task_def.GetMethods();
            MethodInfo cont_with = null;
            int num = 0;
            foreach (var m in cont_with_methods)
            {
                if (m.Name == "ContinueWith" && m.ContainsGenericParameters)
                {
                    var pinfos = m.GetParameters();
                    if (pinfos.Length == 1)
                    {
                        var pinfo = pinfos[0];
                        var ptype = pinfo.ParameterType;
                        var generic_args = ptype.GenericTypeArguments;
                        if (generic_args.Length == 2)
                        {
                            if (generic_args[0].IsGenericType)
                            {
                                if (generic_args[1].IsGenericParameter)
                                {
                                    if (generic_args[0].BaseType == typeof(Task))
                                    {
                                        cont_with = m;
                                        num++;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (num != 1) throw new ApplicationException("ConvertTask: num != 1");

            object ret = null;

            var cont_with_generic = cont_with.MakeGenericMethod(new_task_type);

            var convert_task_proc_method = typeof(TaskUtil).GetMethod(nameof(convert_task_proc), BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(new_task_type);

            var func_type = typeof(Func<,>).MakeGenericType(typeof(Task<>).MakeGenericType(old_task_type), new_task_type);

            Delegate delegate_instance = convert_task_proc_method.CreateDelegate(func_type);

            ret = cont_with_generic.Invoke(src_task_object, new object[] { delegate_instance });

            return ret;
        }

        static TNewResult convert_task_proc<TNewResult>(object t)
        {
            Type old_task_type = t.GetType();
            object result_old = old_task_type.GetProperty("Result").GetValue(t);
            TNewResult result_new = Json.ConvertObject<TNewResult>(result_old);
            return result_new;
        }
    }
}

