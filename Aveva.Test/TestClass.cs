using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
using Aveva.Core.Geometry;
using Aveva.Core.PMLNet;
using System;
using System.Linq;
using TypeFilter = Aveva.Core.Database.Filters.TypeFilter;
namespace ClashChecker;

/// <summary>
/// Класс для теста
/// </summary>
[PMLNetCallable]
public class TestClass
{

    [PMLNetCallable]
    public TestClass()
    {
    }

    [PMLNetCallable]
    public void Assign(TestClass that)
    {
    }

    public string ClashConnectionString { get; set; } = "Data Source=sqltep;Initial Catalog=pdms;Persist Security Info=True;User ID=clashuser;Password=Qgh%fS45Nm;Connection Timeout = 300;TrustServerCertificate=true";


    [PMLNetCallable]
    public void SummarizeVolumes()
    {
        var world = DbElement.GetElement("WORLD");
        string siteName = "/TEMPSITE.L";
        var tempSite = DbElement.GetElement(siteName);
        if (tempSite.IsNull)
        {
            tempSite = world.Create(1, DbElementTypeInstance.SITE);
            tempSite.SetAttribute(DbAttributeInstance.NAME, siteName);
        }
        string zoneName = "/TEMPZONE.L";
        var tempZone = DbElement.GetElement(zoneName);
        if (tempZone.IsNull)
        {
            tempZone = tempSite.Create(1, DbElementTypeInstance.ZONE);
            tempZone.SetAttribute(DbAttributeInstance.NAME, zoneName);
        }
        var volumeName = "/TEMPVOL.L";
        var tempVolume = DbElement.GetElement(volumeName);
        if (tempVolume.IsNull)
        {
            tempVolume = tempZone.Create(1, DbElementTypeInstance.VOLMODEL);
            tempVolume.SetAttribute(DbAttributeInstance.NAME, volumeName);
        }
        //var colZone = new DBElementCollection(new TypeFilter(DbElementTypeInstance.ZONE)).Cast<DbElement>().ToArray();
        DbElement[] colZone = [.. new DBElementCollection(new TypeFilter(DbElementTypeInstance.ZONE)).Cast<DbElement>().Where(e =>
                {
                   DbElement site = e.Owner;
                   string siteName = site.Name();
                   if(siteName.Contains(".L") || siteName.Contains("ZEMI") || siteName.Contains("/po") || site.GetString(DbAttributeInstance.PURP) == "NOCL" || e.GetAsString(DbAttributeInstance.MCOU) == "0")
                       return false;
                   return true;

                })];
        double[] summVolume = [0, 0, 0, 0, 0, 0];

        foreach (var zone in colZone)
        {
            var wVol = zone.GetDoubleArray(DbAttributeInstance.WVOL);
            if (wVol.Length != 6)
                continue;

            ExpandWvol(summVolume, wVol);

        }

        var xSize = summVolume[3] - summVolume[0];
        var ySize = summVolume[4] - summVolume[1];
        var zSize = summVolume[5] - summVolume[2];
        int xLen = 10000;
        int yLen = 10000;
        int zLen = 10000;


        for (int i = 1; i < (int)Math.Round( xSize / xLen, 0); i++)
        {
            for (int j = 1; j < (int)Math.Round(ySize / yLen,0); j++)
            {
                for (int k = 1; k < (int)Math.Round(zSize / zLen,0); k++)
                {
                    var box = tempVolume.Create(1,DbElementTypeInstance.BOX);
                    box.SetAttribute(DbAttributeInstance.POS, Position.Create(summVolume[0] + xSize * i, summVolume[1] + ySize * j, summVolume[3] + zSize * k));
                    box.SetAttribute(DbAttributeInstance.XLEN, xSize);
                    box.SetAttribute(DbAttributeInstance.YLEN, ySize);
                    box.SetAttribute(DbAttributeInstance.ZLEN, zSize);

                }
            }
        }

        

    }


    public void ExpandWvol(double[] wVolToExpand, double[] secondWVol)
    {
        for (int i = 0; i < secondWVol.Length; i++)
        {
            if (i < 4)
                wVolToExpand[i] = Math.Min(wVolToExpand[i], secondWVol[i]);
            wVolToExpand[i] = Math.Max(wVolToExpand[i], secondWVol[i]);
        }
    }

}