
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
        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            string assemblyName = new AssemblyName(args.Name).Name + ".dll";
            string[] assemblyDirectories =
            {
                @"C:\AVEVA\AvevaWorkDLL",
                @"C:\AVEVA\AvevaWorkDLL\CLS\AvevaClash\ViewModelNew\bin\Debug\net481"
            };

            foreach (string assemblyDirectory in assemblyDirectories)
            {
                string assemblyPath = Path.Combine(assemblyDirectory, assemblyName);

                if (File.Exists(assemblyPath))
                    return Assembly.LoadFrom(assemblyPath);
            }

            return null;
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
