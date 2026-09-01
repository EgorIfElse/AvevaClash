
using Aveva.Core.PMLNet;
using ViewForm;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System;
using System.IO;
using System.Reflection;

namespace VIewModelNew
{
    [PMLNetCallable]
    public class VIewModelNew
    {
        static VIewModelNew()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        }
        private static Assembly ResolveAssembly( object sender, ResolveEventArgs args)
        {
            string pluginDirectory = Path.GetDirectoryName(typeof(VIewModelNew).Assembly.Location);

            if (string.IsNullOrWhiteSpace(pluginDirectory))
                return null;

            string assemblyName = new AssemblyName(args.Name).Name + ".dll";

            string assemblyPath = Path.Combine(pluginDirectory, assemblyName);

            return File.Exists(assemblyPath) ? Assembly.LoadFrom(assemblyPath) : null;
        }
        [PMLNetCallable]
        public VIewModelNew()
        {
        }
        [PMLNetCallable]
        public void Assign(VIewModelNew that)
        {
        }
        [PMLNetCallable]
        public void MainShowWpf()
        {

            var window = new MainWindow();
            ElementHost.EnableModelessKeyboardInterop(window);
            window.Show();
        }
    }


}
