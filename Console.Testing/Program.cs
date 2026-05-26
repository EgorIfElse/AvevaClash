using Dapper;
using Microsoft.Data.SqlClient;
namespace ConsoleTesting;

public class Program
{

    private const string sqlUserName = "clashuser";
    private const string sqlPassword = "Qgh%fS45Nm";

    public static void Main(string[] args)
    {



        //Console.WriteLine($"GUID DUPL {syncRecord.NewGuid} | GUID ORIG {syncRecord.OldGuid} | PRIMOBJIDDUPL {syncRecord.NewPrimObjid} | PRIMOBJIDORIG {syncRecord.OldPrimObjid}" );
    }

   //connection.Close();
        //var reader = connection.ExecuteScalar("select top 1 COUNT(*) from clashtableARM");
        ////while(c)
        ////while (reader.Read())
        ////{
        ////    var res = reader.GetValue(0);
        ////foreach (var item in reader)
        ////{
        //    Console.WriteLine(reader);
        ////}

        ////}

        //connection.Close();
    }


//"select id 'Id', clashtype 'ClashType', El1 'FirstElement', type1 'FirstType', usermod1 'FirstUserMode', dept1 'FirstDept', gpset1 'FirstGpset', El2 'SecondElement', type2 'SecondType', usermod2 'SecondUserMode', dept2 'SecondDept', gpset2 'SecondGpset' from clashtableARM");

public record ClashEntity
{
    public int Id { get; set; }
    public string ClashType { get; set; } = "";
    public string FirstElement { get; set; } = "";
    public string FirstType { get; set; } = "";
    public string FirstUserMode { get; set; } = "";
    public string FirstDept { get; set; } = "";
    public string FirstGpset { get; set; } = "";
    public string SecondElement { get; set; } = "";
    public string SecondType { get; set; } = "";
    public string SecondUserMode { get; set; } = "";
    public string SecondDept { get; set; } = "";
    public string SecondGpset { get; set; } = "";
}

public record SetRecord
{


    public string Guid { get; set; }
    public string Name { get; set; }
    public int Original { get; set; }
}
public record SetRecordHastable1
{


    public string Guid { get; set; }
    public string Name { get; set; }
    public int PrimObjId { get; set; }
    public int ParentPrimObjid { get; set; }

}

public record SyncRecord
{
    public int OldPrimObjid { get; set; }
    public int NewPrimObjid { get; set; }
    public int NewParentPrimObjid { get; set; }
    public string OldGuid { get; set; }
    public string NewGuid { get; set; }

    

private static void SyncSets()
    {
        using var connection = new SqlConnection($"Data Source=sqltep;Initial Catalog=Sprut;Persist Security Info=False;User ID=Pdmstotdms;Password=PdMsToTdMs;Pooling=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Command Timeout=0");

        connection.Open();
        string duplicateStageGuid = "{BC868353-B36A-44A1-BBEA-A90EA9AEAB51}";
        string originalStageGuid = "{3FEF1322-7F85-44CD-A41E-DB8222DB721D}";
        int orig = 0;


        var duplicates = connection.Query<SetRecord>("declare @sets table (F_GUID nvarchar(200) , F_NAME nvarchar(200), F_ORIG int );" +
        $"insert into @sets (F_GUID, F_NAME, F_ORIG) (select F_GUID, F_NAME, {orig} from TDMS_TEP.DBO.tobject " +
        "where f_objid in ( select f_linkobjid from TDMS_TEP.DBO.TLinkAttr " +
        "where f_objid in ( select F_LINKOBJID from TDMS_TEP.DBO.TLinkAttr " +
        "where F_objid  in( select F_LINKOBJID from TDMS_TEP.DBO.TLinkAttr " +
        "where F_objid = (select top 1 f_objid from TDMS_TEP.DBO.tobject " +
        $"where f_guid = '{duplicateStageGuid}'))))) select F_NAME 'Name', F_GUID 'Guid', F_ORIG 'Original' from @sets");

        orig = 1;
        var originals = connection.Query<SetRecord>("declare @sets table (F_GUID nvarchar(200) , F_NAME nvarchar(200), F_ORIG int );" +
        $"insert into @sets (F_GUID, F_NAME, F_ORIG) (select F_GUID, F_NAME, {orig} from TDMS_TEP.DBO.tobject " +
        "where f_objid in ( select f_linkobjid from TDMS_TEP.DBO.TLinkAttr " +
        "where f_objid in ( select F_LINKOBJID from TDMS_TEP.DBO.TLinkAttr " +
        "where F_objid  in( select F_LINKOBJID from TDMS_TEP.DBO.TLinkAttr " +
        "where F_objid = (select top 1 f_objid from TDMS_TEP.DBO.tobject " +
        $"where f_guid = '{originalStageGuid}'))))) select F_NAME 'Name', F_GUID 'Guid', F_ORIG 'Original' from @sets");

        List<SyncRecord> syncRecords = [];

        foreach (var duplicate in duplicates)
        {
            var syncDuplicate = connection.Query<SetRecordHastable1>($"select Set_Name 'Name', TDMS_GUID 'Guid', Prim_obj_id 'PrimObjId',Parent_prim_obj_id 'ParentPrimObjId' from TDMS_Notifier.dbo.Hashtable1 where Tdms_guid = '{duplicate.Guid}'");
            if (syncDuplicate.Count() > 1)
            {
                continue;
            }
            int newPrimObjid = syncDuplicate.First().PrimObjId;
            var original = originals.FirstOrDefault(e => e.Name == duplicate.Name);
            var syncOriginal = connection.Query<SetRecordHastable1>($"select Set_Name 'Name', TDMS_GUID 'Guid', Prim_obj_id 'PrimObjId', Parent_prim_obj_id 'ParentPrimObjId' from TDMS_Notifier.dbo.Hashtable1 where Tdms_guid = '{original.Guid}'");
            if (syncOriginal.Count() > 1)
            {
                continue;
            }
            int oldPrimObjid = syncOriginal.First().PrimObjId;


            var syncRecord = new SyncRecord()
            {
                NewGuid = duplicate.Guid,
                OldGuid = original.Guid,
                NewParentPrimObjid = syncDuplicate.First().ParentPrimObjid,
                NewPrimObjid = newPrimObjid,
                OldPrimObjid = oldPrimObjid
            };

            syncRecords.Add(syncRecord);
        }

        foreach (var syncRecord in syncRecords)
        {
            var delete = connection.Execute($"delete from TDMS_NOTIFIER.dbo.Hashtable1 where TDMS_GUID = '{syncRecord.NewGuid}'");
            if (delete == 0)
            {

                continue;
            }
            var update = connection.Execute($"update TDMS_NOTIFIER.dbo.Hashtable1 set Prim_obj_id = {syncRecord.NewPrimObjid}, Parent_Prim_Obj_Id = {syncRecord.NewParentPrimObjid} where Prim_obj_id = {syncRecord.OldPrimObjid}");
            if (update == 0)
            {
                continue;
            }

        }
    }
}

