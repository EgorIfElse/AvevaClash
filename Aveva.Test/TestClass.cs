using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
using Aveva.Core.Geometry;
using Aveva.Core.PMLNet;
using Aveva.Core3D.Clasher;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml.Linq;
using static System.Collections.Specialized.BitVector32;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;
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
/*
--update $!this.tn SET InWorkUser = NULL, InWorkDate = NULL, requestTODept = NULL, requestuser = NULL, requestdate = NULL, approveuser = NULL, approvedate = NULL, approvereason = NULL WHERE inworkuser is not NULL
--select* from $!this.tn WHERE inworkuser is not null
--select* from $!this.tn where existing = 'false'
--update $!this.tn set Existing = 'true'
--id type E1 type1 usermod1 dept1 gpset1 E2 type2 usermod2 dept2 gpset2 date x y z existing RequestTo RequestUser RequestDate ApproveUser ApproveDate ApproveReason InWorkUser InWorkDate
--1  2    3  4      5        6     7     8   9    10       11    12     13   14151617       18        19          20          21          22          23            24         25
    --огород на тему определяния текущего пользователя_TD
	!Session = CURRENT SESSION
	!su = ''
    !su = !!sysuser

    handle any

    endhandle
	
	if !su eq '' then
	!!User = !Session.User().String()
	else
	!!User = !su

    endif

    import 'GridControl'
    handle any
    endhandle
    using namespace 'Aveva.Core.Presentation'

-----------------------------------------------------------------------------------------------------
setup form !!clashviewform resize $* dock right
  bar
  !this.formTitle        = 'тест'
  !this.initcall = '!this.Initcall()'

  path r

    option.selSet at x1 y0 'GPSET' call '!this.SetChange()' width 50 

    button.bUGL 'R' callback  '!this.UpdateGPSETList()'       tooltip 'обновить список комплектов (необходимо если комплект был создан только что и его нет в списке)' 

    path r


    frame.f1 at y0 '      Фильтр дат' width 25
    toggle.tDateFilter at xmin.f1 + 1 ymin.f1 - 0.15 ''  call '!this.show()'
    path d
    text.tA at ymin.f1 + 0.6 'после' width 7 is string
    path r
    text .tB 'до' width 7 is string
    exit


    toggle.tMyDeptOnly 'только моего отдела' call '!this.show()'

    toggle.tHideApproved at xmax.tMyDeptOnly - 5  'прятать согласованные' call '!this.show()'

    toggle.tHideInWork at xmax.tHideApproved - 5 'прятать чужие в работе' call '!this.show()'

    toggle.tOnlyWithRequestForMyDept at xmax.tHideInWork - 5  'только с запросом к моему отделу' call '!this.show()'

	if !!user eq 'SYSTEM' then
      path d
      option .selMyDept at x1 y0.85 'MyDept' call '!this.MyDeptChange()' width 8 $

      path r

      option.selMyUlogID 'MyUlogId' call '!this.MyUlogIdChange()' width 10 $

    endif

    path d
    container .conTableDif nobox PmlNetControl at x1 y2.6 'TableD' anchor R + L + B + T width 180 height 20        
  path d

    button.bCheck 'Проверить' at x1 anchor left + bottom callback  '!this.checkGPSET(!this.SelSet.Selection())'       tooltip 'Проверить этот комплект'

    path r


    button.bApprove 'Согласовать' anchor left + bottom callback  '!this.ApproveClash()'       tooltip 'согласовать выбранные коллизии'

    button.bTakeInWork 'Принять в работу' anchor left + bottom callback  '!this.TakeInWork()'       tooltip 'взять в работу выбранные коллизии'

    button.bSendRequest 'Отправить запрос' anchor left + bottom callback  '!this.sendrequest()'       tooltip 'отправить запрос по выбранным коллизиям'

    button.bTryToPass 'Сдать' anchor left + bottom callback  '!this.trytopass()'       tooltip 'Сдать и заблокировать этот комплект'

    paragraph.pChecked anchor left + bottom text '  '


  member.TableD is NETGRIDCONTROL
  member .datefrom is datetime
  member .dateto is datetime
  member .datatable is array
  member .DBO is DB
  member .datefilter is real
    member .format is DATEFORMAT
    member .conn is string
    member .MyDept is string
    member .MyUlogId is string
    member .CLASHdir is string
    member .checkedsets is array
    member .checkedsetsTIME is array
    member .currGpset is string
    member .tn is string

exit

define method .GetLastProjectCheckDate() is datetime
  !File = object file(!this.CLASHdir & '\lastcheck.txt')
  !lines = !File.ReadFile()
  handle any
    !File.Close()
    !lastcheck = object datetime(1990,1,1)
    return !lastcheck
  endhandle
  !File.Close()

  !lastcheck = object datetime(!lines[1].split()[1].real(),!lines[1].split()[2].real(),!lines[1].split()[3].real())
  return !lastcheck
endmethod

--ищем дату проверки комплекта и возвращаем её, иначе вернём 1990г
define method.getgpsetlastchecktime(!gpset is string) is datetime
  do !i from 1 to !this.checkedsets.size()
    if !this.checkedsets[!i] eq !gpset then
      return !this.checkedsetsTIME[!i]
      break
    endif
  enddo
  !now = object datetime()
  return !now
endmethod

define method .isGreenGpset(!gpset is string) is boolean
  if !gpset eq 'ALL' or !gpset eq 'CE' then
    return false
  endif
  
  !gp = !gpset.dbref()
  handle any
    $P $!gpset - это не комплект
    return false
  elsehandle none
  --если отделы не совпали и это не сочетание отдел=ОГС и комплект=СОТ
  if !!GetDepartment(!gpset.dbref(),'GPSET') neq !this.myDept and not (!!GetDepartment(!gpset.dbref(),'GPSET') eq 'SOT' and !this.myDept eq 'OGS' ) then
    $P $!gpset - это комплект другого отдела
    return false
  endif
  endhandle

  !retval = false
  !now = object datetime()
  --вернёт истину если комплект не нуждается в проверке
  --это будет если комплект модифицировался до чека (то есть не менялся после чека)
  --при этом чек не позже чем 3 дня назад??
  !gpsetlastmod = !this.getgpsetLastmod(!gpset)
  !deltaChange = !!DateSubstract( !now.date() , !now.month(), !now.year(), !gpsetlastmod.date(), !gpsetlastmod.month(), !gpsetlastmod.year() )
  $P комплект последний раз изменялся $!gpsetlastmod ($!deltaChange дней назад)
  !LPCD = !this.GetLastProjectCheckDate()
  
  !LC = !LPCD
  --isManualChecked возвращает true если был проверен вручную
  if !this.isManualChecked(!gpset) then
  --getgpsetlastchecktime возвращает дату последней проверки (тоже что isManualChecked только с датой)
    !gpsetlastCheckTime = !this.getgpsetlastchecktime(!gpset)
	--если дата ручной проверки после ночной (как правило это так) то дата последней проверки это и есть дата ручной проверки
    if !LPCD.lt(!gpsetlastCheckTime) then
	  !LC = !gpsetlastCheckTime
    endif
  endif
  --в данный момент известна дата последней проверки !LC
  !delta = !!DateSubstract( !now.date() , !now.month(), !now.year(), !LC.date(), !LC.month(), !LC.year() )
  $P комплект проверялся $!delta дней назад
  --теперь условие если комплект менялся после !LC то однозначано надо перепроверять
  if !LC.lt(!gpsetlastmod) then
    !retval = false
  else
    --тут то проверка что последний раз проверлся не более 2х дней назад ()
	if !delta le 2 then
	  !retval = true
	else
	  !retval = false
	endif
  endif
  
  return !retval
endmethod

define method .getGPSETlastmod(!gpset is string) is datetime
  if !gpset eq 'ALL' then
  !now = object datetime()
    return !now
  endif
  --получаем lastmod hier комплекта
  !lastmod = object datetime( lastmod hier of $!gpset )
  --бежим в цикле по элементам комплекта спрашиваем lastmod hier of sitem и если ПОЗЖЕ чем !lastmod то перезаписываем !lastmod
  var !gpitemcol collect all gpitem for $!gpset
  do !J values !gpitemcol
    !lm = object datetime( lastmod hier of sitem of $!J )
    if !lm.GT(!lastmod) then
      !lastmod = !lm
    endif
  enddo
  return !lastmod
endmethod

--isManualChecked это значит проверен вручную
define method .isManualChecked(!gpset is string) is boolean
  !retval = false
  do !i from 1 to !this.checkedsets.size()
    if !this.checkedsets [!i]
eq !gpset then
      !retval = true
      break
    endif
  enddo
  return !retval
endmethod


define method .UpdateCheckedStatus()
  --ищем в массиве недавно проверенных комплектов текущий, и если нашли красим
  --это специальный массив который изначально пустой он содержит имена элементов которые тока что проверены. он пополняется после чека
  !this.pChecked.background = 1
  if !this.isGreenGpset(!this.selset.selection()) then
    !this.pChecked.background = 5
  endif
endmethod

define method .SetChange()
  --меняем цвет кнопки
  !this.UpdateCheckedStatus()
  !this.show()
endmethod

define method .showselectedclash()
  !rows = !this.TableD.getselectedrows()
  if !rows.size() eq 0 then
    !!alert.message('Не выбраны коллизии для добавления на экран')
  endif
  do !i from 1 to !rows.size()
    !e1 = !rows [!i][3]
    !e2 = !rows[!i][8]
    add $!e1
    --если моего отдела то в красный, чужой в синий
    !d1 = !rows[!i][6]
    $P $!d1
    if !rows[!i][6] eq !this.mydept then
      enhance $!e1 col red
    else
      enhance $!e1 col blue
    endif


    add $!e2
    !d2 = !rows[!i][11]
    $P $!d2
    if !rows[!i][11] eq !this.mydept then
      enhance $!e2 col red
    else
      enhance $!e2 col blue
    endif
  enddo
endmethod

define method.showNumbersOfSelectedClash()
  !rows = !this.TableD.getselectedrows()
  if !rows.size() eq 0 then
    !!alert.message('Не выбраны коллизии для отображения номеров')
  endif
  do !i from 1 to !rows.size()
    !x = !rows [!i][14]
	  !y = !rows[!i][15]
	                         !z = !rows[!i][16]
    AID TEXT NUMBER 66 '$!rows[$!i][1]' AT E $!x N $!y U $!z
  enddo
endmethod


define method.MyDeptChange()
  !this.MyDept = !this.selMyDept.selection()
  !this.titleupdate()

  var !ulogidcol collect all ulogid with (namn of usef eq '$!this.MyDept')
  var !ulogidNAMN evaluate namn for all from !ulogidcol
  var !currentUser evar USERNAME
  !ulogidNAMN.append(!currentUser)
  --!UlogIdArray = array()
  !this.selMyUlogId.Dtext = !ulogidNAMN
  !this.selMyULogId.Rtext = !ulogidNAMN
  
  !this.MYULOGIDCHANGE()
  !this.titleupdate()
  
  --запомнить текущий комплект
  !oldgpset = !this.selSet.selection()
  !this.updateGpsetList()
  !this.selSet.select('Rtext','ALL')
endmethod

define method .MYULOGIDCHANGE()
  !this.MyUlogId = !this.selMyUlogId.selection()
  !this.titleupdate()
endmethod

define method .titleupdate()
  !this.formTitle        = 'коллизии по ' & !this.currgpset & ' для...  ОТДЕЛ: ' & !this.MyDept & ' | пользователь: ' & !this.MyUlogId
  handle any
    !this.formTitle        = 'коллизии для...  ОТДЕЛ: ' & !this.MyDept & ' | пользователь: ' & !this.MyUlogId
  endhandle
endmethod

---------------------------------------------------------------
define method .aftselch(!data is ARRAY)
   !SelEl = !data[0][1]
    handle ANY
      return
    endhandle
    $!SelEl
    handle ANY
      return
    endhandle
endmethod


define method.show()
	using namespace 'Aveva.Core.Presentation'

 -- конец записи
  !ARRAYdelta = object ARRAY()
  
  !this.currGpset = !this.SelSet.Selection()
  q var !this.currGpset
  !gpset = !this.currgpset
  q var !gpset
  if !this.currGpset eq 'CE' then
    !this.currGpset = name of ce
  endif
  
  !this.titleupdate()
  !wherestring = | where 1 = 1|
  
  if !gpset neq 'ALL' and !gpset neq 'CE' then
    !wherestring = !wherestring & | and(gpset1 = '$!gpset' or gpset2 = '$!gpset') |
  endif
  
  if !this.tMyDeptOnly.val then
    !wherestring = !wherestring & | and(dept1 = '$!this.MYDEPT' or dept2 = '$!this.MYDEPT') |
  endif
  
  if !this.tOnlyWithRequestForMyDept.val then
    !wherestring = !wherestring & | and(RequestToDept = '$!this.mydept') |
  endif
  
  if !this.tHideApproved.val then
    !wherestring = !wherestring & | and(approveReason is null or approveReason = '') |
  endif
  
  if !this.tHideInWork.val then
  !wherestring = !wherestring & | and not (requestToDept<> '$!this.mydept' and (InWorkUser is not null ))|
  endif
  
  if !this.tDateFilter.val then
    --получить дату из текста 
	!dateA =  !this.tA.val.split('.')[2] & '.' & !this.tA.val.split('.')[1] & '.' & !this.tA.val.split('.')[3]
	!dateB =  !this.tB.val.split('.')[2] & '.' & !this.tB.val.split('.')[1] & '.' & !this.tB.val.split('.')[3]
	$P $!dateA $!dateB
    !wherestring = !wherestring & | and date >= '$!dateA' and date <= '$!dateB' |
  endif
  
  --$p SQL запрос начало
  !dtDO = object datetime()
  !secDO = !dtDO.second() + !dtDO.minute()* 60 + !dtDO.hour()* 3600
  --получаем массив sqlarray или функцией для CE или прям обычным select
  if !gpset eq 'CE' then

    !sqlarray = !!QueryClashByEl(!!ce,!wherestring.replace('where','and') )
  else

    !query = |select id, clashtype, El1, type1, usermod1, dept1, gpset1, El2, type2, usermod2, dept2, gpset2, date, x, y, z, existing, RequestToDept, RequestUser, RequestDate, ApproveUser, ApproveDate, ApproveReason, InWorkUser, InWorkDate from $!this.tn $!wherestring|
    -- !query = | select id, clashtype, El1, type1, usermod1, dept1, gpset1, El2, type2, usermod2, dept2, gpset2, date, x, y, z, existing, RequestToDept, RequestUser, RequestDate, ApproveUser, ApproveDate, ApproveReason, InWorkUser, InWorkDate from $!this.tn $!wherestring|
    --q var !query
    --!sqlarray = array()
    !sqlarray = !!sqlQuery('SQL', !this.conn, !query)

        --[+] 23.12.2025 Калинин Г.Д.добавил пост-обработку данных по коллизиям (выбранный пользователем GPSET всегда будет фигурировать как "gpset 1")
		if(!gpset neq 'ALL') then 
			do !sqlRow values !sqlArray

                skip if !sqlRow[11] neq !gpset
                !tmp = !sqlRow[6]
				!sqlRow[6] = !sqlRow[11]
				!sqlRow[11] = !tmp

            enddo

        endif
  endif

  !dtPOSLE = object datetime()
  !secPOSLE = !dtPOSLE.second() + !dtPOSLE.minute()* 60 + !dtPOSLE.hour()* 3600
  !res = !secPOSLE - !secDO

  !dtDO = object datetime()
  !secDO = !dtDO.second() + !dtDO.minute()* 60 + !dtDO.hour()* 3600

 do !i from 0 to !sqlarray.size() - 1
   !row = array()
   do !j from 0 to !sqlarray[!i].size() - 1
     !row.append(!sqlarray[!i][!j])
   enddo

   !ARRAYdelta.append(!row)
 enddo
 
  !dtPOSLE = object datetime()
  !secPOSLE = !dtPOSLE.second() + !dtPOSLE.minute()* 60 + !dtPOSLE.hour()* 3600
  !res = !secPOSLE - !secDO

  var !prj proj id
  if !prj eq 'GCC' then
    !headings = split('id type E1 type1 usermod1 dept1 LogicalOwner1 E2 type2 usermod2 dept2 LogicalOwner2 date x y z existing RequestTo RequestUser RequestDate ApproveUser ApproveDate ApproveReason InWorkUser InWorkDate')
  else
    !headings = split('id type E1 type1 usermod1 dept1 gpset1 E2 type2 usermod2 dept2 gpset2 date x y z existing RequestTo RequestUser RequestDate ApproveUser ApproveDate ApproveReason InWorkUser InWorkDate')
  endif
  
  !ndsd = object NETDATASOURCE('Изменения', !headings, !ARRAYdelta)
  !this.TableD.bindToDataSource(!ndsd)
  !this.tabled.SETCOLUMNCOLOR(6,'Yellow')
  !this.tabled.SETCOLUMNCOLOR(11,'Yellow')

  !this.tabled.SETCOLUMNVISIBILITY(14, false) $* X
  !this.tabled.SETCOLUMNVISIBILITY(15, false) $* Y
  !this.tabled.SETCOLUMNVISIBILITY(16, false) $* Z

endmethod

define method .updategpsetlist()
	!SetName = object ARRAY()
	!SetName.append('CE')
	!SetName.append('ALL')

  !sqlarray = array()
  var !proj proj code
  if !proj neq 'GCC' then
    var !Set collect all GPSET with (ddep eq 4 AND PURP OF GPWL EQ 'KOMP' and mcount neq 0)
	--q var !Set
    do !x values !Set
      !SetName.Append(!x.dbref().name)
    enddo

    !SetName.Sort()
    !tn = !this.tn
    
    if !proj.eq('ARM') then  
    
       $* заплатка. 25/06/2025 так хотя бы работает.
       !query = ||
       !query = !query & | select clashtableARM1.gpset1|
       !query = !query & | ,count (CASE WHEN (not isnull(clashtableARM1.approvereason,'!') = '!' and not clashtableARM1.approvereason = '') THEN 1 ELSE NULL end) as sogl|
       !query = !query & | ,count (CASE WHEN (isnull(clashtableARM1.approvereason,'!') = '!' or clashtableARM1.approvereason = '') THEN 1 ELSE NULL end ) as nesog|
       !query = !query & | , count (CASE WHEN ((clashtableARM1.dept1) = 'SYSTEM' or (clashtableARM1.dept2) = 'SYSTEM') and (isnull(clashtableARM1.approvereason,'!') <> '!' and clashtableARM1.approvereason <> '') THEN 1 ELSE NULL end )  as SOG_myotd |
       !query = !query & | , count (CASE WHEN ((clashtableARM1.dept1) = 'SYSTEM' or (clashtableARM1.dept2) = 'SYSTEM') and (isnull(clashtableARM1.approvereason,'!') = '!' or clashtableARM1.approvereason = '') THEN 1 ELSE NULL end )  as NESOG_myotd |
       !query = !query & | From |
       !query = !query & | (SELECT Dept1, Gpset1,  Dept2,  ApproveReason |                    
       !query = !query & | FROM clashtableARM |
       !query = !query & | Union ALL |
       !query = !query & | SELECT Dept1, Gpset2,  Dept2,  ApproveReason |                    
       !query = !query & | FROM clashtableARM where Gpset2 <> Gpset1) clashtableARM1 group by clashtableARM1.gpset1 order by 1 |
       
	   !sqlarray = !!sqlQuery('SQL',!this.conn,!query) 
    
    elseif !proj.eq('TUY') then 
    
       $* заплатка. 27/06/2025 так хотя бы работает.
       !query = ||
       !query = !query & | select clashtableTUY1.gpset1|
       !query = !query & | ,count (CASE WHEN (not isnull(clashtableTUY1.approvereason,'!') = '!' and not clashtableTUY1.approvereason = '') THEN 1 ELSE NULL end) as sogl|
       !query = !query & | ,count (CASE WHEN (isnull(clashtableTUY1.approvereason,'!') = '!' or clashtableTUY1.approvereason = '') THEN 1 ELSE NULL end ) as nesog|
       !query = !query & | , count (CASE WHEN ((clashtableTUY1.dept1) = 'SYSTEM' or (clashtableTUY1.dept2) = 'SYSTEM') and (isnull(clashtableTUY1.approvereason,'!') <> '!' and clashtableTUY1.approvereason <> '') THEN 1 ELSE NULL end )  as SOG_myotd |
       !query = !query & | , count (CASE WHEN ((clashtableTUY1.dept1) = 'SYSTEM' or (clashtableTUY1.dept2) = 'SYSTEM') and (isnull(clashtableTUY1.approvereason,'!') = '!' or clashtableTUY1.approvereason = '') THEN 1 ELSE NULL end )  as NESOG_myotd |
       !query = !query & | From |
       !query = !query & | (SELECT Dept1, Gpset1,  Dept2,  ApproveReason |                    
       !query = !query & | FROM clashtableTUY |
       !query = !query & | Union ALL |
       !query = !query & | SELECT Dept1, Gpset2,  Dept2,  ApproveReason |                    
       !query = !query & | FROM clashtableTUY where Gpset2 <> Gpset1) clashtableTUY1 group by clashtableTUY1.gpset1 order by 1 |
	   !sqlarray = !!sqlQuery('SQL',!this.conn,!query)     
	   
     elseif !proj.eq('UYK') then 
    
       $* заплатка. 23/09/2025 так хотя бы работает.
       !query = ||
       !query = !query & | select clashtableUYK1.gpset1|
       !query = !query & | ,count (CASE WHEN (not isnull(clashtableUYK1.approvereason,'!') = '!' and not clashtableUYK1.approvereason = '') THEN 1 ELSE NULL end) as sogl|
       !query = !query & | ,count (CASE WHEN (isnull(clashtableUYK1.approvereason,'!') = '!' or clashtableUYK1.approvereason = '') THEN 1 ELSE NULL end ) as nesog|
       !query = !query & | , count (CASE WHEN ((clashtableUYK1.dept1) = 'SYSTEM' or (clashtableUYK1.dept2) = 'SYSTEM') and (isnull(clashtableUYK1.approvereason,'!') <> '!' and clashtableUYK1.approvereason <> '') THEN 1 ELSE NULL end )  as SOG_myotd |
       !query = !query & | , count (CASE WHEN ((clashtableUYK1.dept1) = 'SYSTEM' or (clashtableUYK1.dept2) = 'SYSTEM') and (isnull(clashtableUYK1.approvereason,'!') = '!' or clashtableUYK1.approvereason = '') THEN 1 ELSE NULL end )  as NESOG_myotd |
       !query = !query & | From |
       !query = !query & | (SELECT Dept1, Gpset1,  Dept2,  ApproveReason |                    
       !query = !query & | FROM clashtableUYK |
       !query = !query & | Union ALL |
       !query = !query & | SELECT Dept1, Gpset2,  Dept2,  ApproveReason |                    
       !query = !query & | FROM clashtableUYK where Gpset2 <> Gpset1) clashtableUYK1 group by clashtableUYK1.gpset1 order by 1 |
	   !sqlarray = !!sqlQuery('SQL',!this.conn,!query)  
    
    else

    !query = |select t2.gpset1,|
    !query = !query & |count (CASE WHEN (not isnull(| & !tn & |.approvereason,'!') = '!' and not | & !tn & |.approvereason = '') THEN 1 ELSE NULL end) as sogl,|
    !query = !query & |count (CASE WHEN (isnull(| & !tn & |.approvereason,'!') = '!' or | & !tn & |.approvereason = '') THEN 1 ELSE NULL end ) as nesog, |
    !query = !query & |count (CASE WHEN ((| & !tn & |.dept1) = '$!this.myDept' or (| & !tn & |.dept2) = '$!this.myDept') and (isnull(| & !tn & |.approvereason,'!') <> '!' and | & !tn & |.approvereason <> '') THEN 1 ELSE NULL end )  as SOG_myotd, |
    !query = !query & |count (CASE WHEN ((| & !tn & |.dept1) = '$!this.myDept' or (| & !tn & |.dept2) = '$!this.myDept') and (isnull(| & !tn & |.approvereason,'!') = '!' or | & !tn & |.approvereason = '') THEN 1 ELSE NULL end )  as NESOG_myotd |
    !query = !query & |from (select distinct gpset1 from ( |
    !query = !query & |select distinct gpset1 from | & !tn & | UNION ALL |
    !query = !query & |select distinct gpset2 from $!tn) as t1) as t2 ,$!this.tn where (t2.gpset1 = | & !tn & |.gpset1 or t2.gpset1 = | & !tn & |.gpset2)  group by t2.gpset1|
 
	!sqlarray = !!sqlQuery('SQL',!this.conn,!query) 
        
    endif

  endif
  
    !SetText = array()
    --внешний цикл по массиву комплектов
    do !i from 1 to !SetName.size()
      !SetText [!i]  = !setname [!i] & ' общее(согл:0/несог:0) | для моего отдела(согл:0/несог:0)'
      do !J from 0 to !sqlarray.size() - 1
        if !SetName [!i]
eq !sqlarray [!J][0].trim('LR') then
      !SetText[!i] = !setname[!i] & ' общее(согл:' & !sqlarray[!J][1] & '/несог:' & !sqlarray[!J][2] & ') | для моего отдела(согл:' & !sqlarray[!J][3] & '/несог:' & !sqlarray[!J][4] & ')'
      break
    	  endif
      enddo
    enddo
    !this.selSet.Dtext = !SetText
    !this.selSet.Rtext = !SetName

    !SetText.Clear()
    !SetName.Clear()
endmethod
define method .tShow3D()
  --прячем или показываем форму 3D в зависимости от галочки
  !s = !this.3DWindowMenu.fieldproperty('show3DWindow','SELECTED')
  if !s then
    show !!clash3dView
  else
    hide !!clash3dView
  endif
endmethod
---------------------------------------------------
define method .clashviewform()
  var !proj proj id
  if !proj eq 'GCC' then
    --!this.bCheck.visible = false
    !this.bTryToPass.visible = false
    !this.bUGL.visible = false
  endif

	!this.bar.add('Навигация', 'NaviMenu')
    !this.bar.add('3D-Окно', '3DWindowMenu')
    !this.bar.add('Уведомления', 'MailMenu')
    !this.bar.add('Справка', 'HelpMenu')


    if !!user eq 'SYSTEM' then
        !this.bar.add('Для администратора', 'SystemMenu')

    endif

    -- File menu
  !menu = !this.newMenu('NaviMenu')
  !menu.add('callback', 'Перейти к 1', '!this.gotoel1()')
  !menu.add('callback', 'Перейти к 2', '!this.gotoel2()')
  !menu.add('callback', 'Центрироваться на выбранной', '!this.centerThisClash()')
    !menu.add('callback', 'Очистить 3D вид', 'rem all')
    !menu.add('callback', 'Добавить выбранные в 3D', '!this.showSelectedClash()')
    !menu.add('callback', 'Пронумеровать выбранные в 3D', '!this.showNumbersOfSelectedClash()')
    !menu.add('callback', 'Убрать все номера из 3D', 'aid clear all 66')
    !menu = !this.newMenu('3DWindowMenu')
    !menu.add('TOGGLE', 'показывать 3D в отдельном окне', '!this.tShow3D()', 'show3DWindow')
    !menu.add('callback', 'Export to Excel',  | !this.ExportToExcel() |)

    !menu = !this.newMenu('MailMenu')
    !menu.add('callback', 'Уведомить о запросах на согласование ВЕСЬ КОМПЛЕКТ',  | !this.SendMailByRequest('ALL') |)
    !menu.add('callback', 'Уведомить о запросах на согласование ВЫБРАННЫЕ КОЛЛИЗИИ',  | !this.SendMailByRequest('SEL') |)

    !menu = !this.newMenu('HelpMenu')
    !menu.add('callback', 'Видео',  | syscom 'explorer "\\tep-m.ru\data\App\Справочная информация\Служба информационных технологий\ClashManager видео\новый" &' |)

    !menu.add('callback', 'Инструкция',  | !this.gotodoc() |)

    !menu = !this.newMenu('SystemMenu')
    !menu.add('callback', 'Проверка флага Existings',  | show!!projclashlist |)

  !this.CLASHdir = !!getclashdir()
  !this.conn = !!GetClashSqlConn()
  !this.tn = !!GetClashTableName()


  var!user user
  var!team team
  !this.MyDept = !!user
  !find = !!findMatchwild(!team.split(), !user)
     if !find.Empty().not() then
        !index = !find.MinIndex()
        !this.MyDept = !team.split()[!find[!index]]

     else
    !this.MyDept = !team.split()[1]
     endif
  var !login login
  !this.MyUlogId = !login
  !this.titleupdate()

  !this.tMyDeptOnly.val = true

  var!usercol collect all user
  var!deptArray evaluate namn for all from !usercol

  if !!user eq 'SYSTEM' then
    !this.selMyDept.Dtext = !deptArray
    !this.selMyDept.Rtext = !deptArray
  endif

    --создаём файл быз если его нет

    !!CreateClashDbIfNotExist2()

    !this.UpdateGPSETList()
    !this.selSet.select('Rtext', 'ALL')
    !this.UpdateCheckedStatus()

  !this.format = object DATEFORMAT('T M.D.Y')
  !this.format.month('INTEGER')
  !this.format.year(4)

  using namespace 'Aveva.Core.Presentation'
  !this.TableD = object NETGRIDCONTROL()

  !this.conTableDif.control  = !this.TableD.handle()
  !this.TableD.addEventHandler('onPopup', !this, 'popupMeth')
  !this.TableD.addEventHandler('AfterSelectChange', !this, 'AfterSelectChangeCallback')
  !this.TableD.HIDEGROUPBYBOX(true)
  !this.TableD.fixedHeaders(false)
  !this.TableD.outlookGroupStyle(false)
  !this.TableD.headerSort(false)
  !this.TableD.columnSummaries(false)
  !this.TableD.singleRowSelection(false)    
  !this.TableD.setAlternateRowColor('WHITE')
  !this.TableD.editableGrid(false)  
  !this.TableD.clearGrid()
  !this.TableD.setNameColumnImage()    
  !this.TableD.splitGrid(false)
  !this.TableD.OutlookGroupStyle(true)

  !popup = !this.newMenu('popupMeth')
  !popup.add('callback', 'перейти к 1', '!this.gotoel1()')
  !popup.add('callback', 'перейти к 2', '!this.gotoel2()')
  !popup.add('callback', 'центрироваться на этой', '!this.centerThisClash()')
  !popup.add('callback', 'добавить и раскрасить выделенные', '!this.showSelectedClash()')
  if !!user eq 'SYSTEM' then
    !popup.add('callback', 'проверить на существование(CLASH CHECK этих 2х)', '!this.CheckThisClash()')
    !popup.add('callback', 'проверить по старой базе', '!this.CheckByOldBase()')
    !popup.add('callback', 'сбросить запросы и согласования', '!this.ResetStatusOfThisClash()')
    !popup.add('callback', 'проверить на существование элементов (IsNeedToDeleteClash)', '!this.CheckExistElement()')
  endif
  !popup.add('callback', 'отправить запрос', '!this.sendrequest()')
  !popup.add('callback', 'Согласовать', '!this.ApproveClash()')
  !popup.add('callback', 'Взять в работу', '!this.TakeInWork()')

  !this.conTableDif.popup = !popup

  !now = object datetime() $*текущее время
  !todaymidnightSTR = !now.date() & '.' & !now.month() & '.' & !now.year() $*строка в формате сервера(месяц сначала)
  !this.tA.val = !todaymidnightSTR
  !this.tB.val = !todaymidnightSTR


endmethod

define method.initcall()

endmethod


define method.AfterSelectChangeCallback(!a is ARRAY)
   if !a.unset() then
     return
   endif
   --если формs вобще нет, то выходим
   if not defined(!!clash3dView) then
     return
   endif
   --если форма отображена то чо - то делаем
   if !!clash3dView.SHOWN() then
     !this.ShowThisClash()
   endif
endmethod

define method .gotodoc()
  syscom '"\\tep-m.ru\data\App\PDMS\PDMS_TEP\TUNINGE3D\PMLLIB\design\ClashForE3D\И 00-16.312-2013 Программа проверки на коллизии (версия 3.0) в PDMS.pdf" &'
endmethod

define method.centerThisClash()
  !sr = !this.TableD.getselectedrows()
  !center[1] = !sr[1][14].real()
  !center[2] = !sr[1][15].real()
  !center[3] = !sr[1][16].real()

  !!gph3ddesign1.view.THROUGH = !center
endmethod

define method.ShowThisClash()
  !sr = !this.TableD.getselectedrows()

  !el1 = !sr[1][3]
  !el2 = !sr[1][8]
  !AIDString = !sr[1][1] & ' ' & !sr[1][2]
  !!clash3dView.formtitle = !AIDString
  !!prepareclashscreen2(!el1, !el2, false, false, 'E $!sr[1][14] N $!sr[1][15] U $!sr[1][16]', !AIDString, !!clash3dview.myview, !!clash3dview.drawlist)
  !!clash3dview.myview.refresh()
endmethod

define method.SendMailByRequest(!opt is string) $*из контекстного меню
  if !opt eq 'SEL' then
    !sr = !this.TableD.getselectedrows()
  elseif!opt eq 'ALL' then
    !sr = !this.TableD.GETROWS()
  else
    !!alert.message('ошибка. сообщение не отправлено.')
  endif
  var !login login
  !umarray = array()
  !idarray = array()
  !deptsarray = array()
  do !i from 1 to!sr.size()
    !id = !sr[!i][1]
    --отправляет запрос к тому кто не "мы" и где есть запрос не к нам и где нет ни взятия в работу ни согасования
    !dept1 = !sr[!i][6]
    !dept2 = !sr[!i][11]
    if !dept2.Trim().eq('') or!dept2.unset() then
    endif
    $*21 / 08 / 2025 надо тоже lowcase!!!
    !um1 = !sr[!i][5].lowcase()
    !um2 = !sr[!i][10].lowcase()
    if !dept1 eq!this.mydept then
      !requesttodept = !dept2
      !mailuser = !um2



      var!proj project code

      --для импортированных из оллплана объектов, по архитектуре отправлять письма Драчеву, по стройке Сухареву, у импортированных объектов автор скрывается под именем pdmsadmin
      if !um2 inset(lowcase('GoncharenkoEA'), lowcase('KarpenkoIA'), lowcase('admin'), lowcase('DubininYV'), lowcase('NikitinMD'), lowcase('demo'), lowcase('BalashovAN')) and!requesttodept.matchwild('*ARX*') then
        !mailuser = 'drachevai'
      elseif!um2 inset(lowcase ('GoncharenkoEA'), lowcase('KarpenkoIA'), lowcase('admin'), lowcase('DubininYV'), lowcase('NikitinMD'), lowcase('demo'), lowcase('BalashovAN') ) and!requesttodept.matchwild('*SOT*') then
        !mailuser = 'suharev'
      elseif!um2 inset(lowcase ('GoncharenkoEA'), lowcase('KarpenkoIA'), lowcase('admin'), lowcase('DubininYV'), lowcase('NikitinMD'), lowcase('demo'), lowcase('BalashovAN') )and!requesttodept.matchwild('*OIV*') then

        if !proj eq 'TUY' then
        !mailuser = 'zolotov'

        else
    !mailuser = 'tsarkovos'

        endif
      elseif !um2 inset(lowcase('GoncharenkoEA'), lowcase('KarpenkoIA'), lowcase('admin'), lowcase('DubininYV'), lowcase('NikitinMD'), lowcase('demo'), lowcase('BalashovAN')) and!requesttodept.matchwild('*VIK*') then
        !mailuser = 'Korolkova'
		$*03 / 10 / 2025 GoncharenkoEA Не было ответственного за ОМК, добавил Ляна
      elseif !um2 inset(lowcase('GoncharenkoEA'), lowcase('KarpenkoIA'), lowcase('admin'), lowcase('DubininYV'), lowcase('NikitinMD'), lowcase('demo'), lowcase('BalashovAN')) and!requesttodept.matchwild('*OMK*') then
        !mailuser = 'lyanas'

      elseif!um2 inset(lowcase ('GoncharenkoEA'), lowcase('KarpenkoIA'), lowcase('admin'), lowcase('DubininYV'), lowcase('NikitinMD'), lowcase('demo'), lowcase('BalashovAN') ) and!requesttodept.matchwild('*TMO*') then
        !mailuser = 'denisov'

         endif

        !dt = object datetime()
        !date = !this.format.string(!dt)
      -- 29 / 05 / 2025
      --для объектов выполненных ЭННОВА:
      !ennova = !sr[!i][8].Dbref().Dbfile.SubString(2, 8) inset('%TUE000%', '%YKE000%')
      if !ennova and!um2 inset(lowcase ('yakovleva'), lowcase('egorov'), lowcase('guseva'), lowcase('shetko'), lowcase('olennikova'), lowcase('lyubetskaya'), lowcase('vandanova') ) then
      !mailuser = 'koveshnikovva'
      !dept2 = 'TMO'
      !requesttodept = 'TMO'
      !query = | update $!this.tn SET requesttodept = '$!requesttodept', dept2 = '$!dept2', usermod2 = '$!mailuser', requestdate = '$!date' WHERE id = '$!sr[$!i][1]' and(requesttodept IS NULL OR requesttodept = '') AND(dept2 IS NULL OR dept2 = '') |
      !sqlarray = !!sqlQuery('SQL', !this.conn, !query)
      endif

      !ennova = !sr[!i][8].Dbref().Dbfile.SubString(2, 8) inset('%TUE000%', '%YKE000%')
      if !ennova and!um2 inset(lowcase ('gusev') ) then
      !mailuser = 'Korolkova'
      !dept2 = 'VIK'
      !requesttodept = 'VIK'
      !query = | update $!this.tn SET requesttodept = '$!requesttodept', dept2 = '$!dept2', usermod2 = '$!mailuser', requestdate = '$!date' WHERE id = '$!sr[$!i][1]' and(requesttodept IS NULL OR requesttodept = '') AND(dept2 IS NULL OR dept2 = '') |
      !sqlarray = !!sqlQuery('SQL', !this.conn, !query)
      endif

      -- 01 / 12 / 2025
      --для объектов выполненных Энергоремонт:
      !EnergoRemont = !sr[!i][8].Dbref().Dbfile.SubString(2, 8) inset('%YKR000%')
      if !EnergoRemont and!um2 inset(lowcase ('Пользователь') ) then
      !mailuser = 'hannanovaaa'
      !dept2 = 'ASU'
      !requesttodept = 'ASU'
      !query = | update $!this.tn SET requesttodept = '$!requesttodept', dept2 = '$!dept2', usermod2 = '$!mailuser', requestdate = '$!date' WHERE id = '$!sr[$!i][1]' and(requesttodept IS NULL OR requesttodept = '') AND(dept2 IS NULL OR dept2 = '') AND usermod2 = 'Пользователь' |
      !sqlarray = !!sqlQuery('SQL', !this.conn, !query)
      endif

      -- 01 / 12 / 2025
      --для объектов выполненных c 123:
      !EnergoRemont = !sr[!i][8].Dbref().Dbfile.SubString(2, 8) inset('%UKB000%')
      !siteDept = !sr[!i][8].dbref()
      !s = site of $!siteDept
      if !EnergoRemont and!um2 inset( '123' ) and!s.name.SubString(8, 2) inset('TD') then
      !mailuser = 'chertkovav'
      !dept2 = 'TMO'
      !requesttodept = 'TMO'
      !query = | update $!this.tn SET requesttodept = '$!requesttodept', dept2 = '$!dept2', usermod2 = '$!mailuser', requestdate = '$!date' WHERE id = '$!sr[$!i][1]' and(requesttodept IS NULL OR requesttodept = '') AND(dept2 IS NULL OR dept2 = '') AND usermod2 = '123' |
      !sqlarray = !!sqlQuery('SQL', !this.conn, !query)
      endif

    endif





    if !dept2 eq!this.mydept then
      !requesttodept = !dept1
      !mailuser = !um1
       if !um1 inset(lowcase('GoncharenkoEA'), lowcase('KarpenkoIA'), lowcase('admin'), lowcase('DubininYV'), lowcase('NikitinMD'), lowcase('demo'), lowcase('BalashovAN')) and!requesttodept.matchwild('*ARX*') then
        !mailuser = 'drachevai'
      elseif!um1 inset(lowcase ('GoncharenkoEA'), lowcase('KarpenkoIA'), lowcase('admin'), lowcase('DubininYV'), lowcase('NikitinMD'), lowcase('demo'), lowcase('BalashovAN') ) and!requesttodept.matchwild('*SOT*') then
        !mailuser = 'suharev'
		$*03 / 10 / 2025 GoncharenkoEA Не было ответственного за ОМК, добавил Ляна
      elseif !um1 inset(lowcase('GoncharenkoEA'), lowcase('KarpenkoIA'), lowcase('admin'), lowcase('DubininYV'), lowcase('NikitinMD'), lowcase('demo'), lowcase('BalashovAN')) and!requesttodept.matchwild('*OMK*') then then
        !mailuser = 'lyanas'
      elseif!um1 inset(lowcase ('GoncharenkoEA'), lowcase('KarpenkoIA'), lowcase('admin'), lowcase('DubininYV'), lowcase('NikitinMD'), lowcase('demo'), lowcase('BalashovAN') ) and!requesttodept.matchwild('*OIV*') then

        if !proj eq 'TUY' then
        !mailuser = 'zolotov'

        else
    !mailuser = 'tsarkovos'

        endif
      elseif !um1 inset(lowcase('GoncharenkoEA'), lowcase('KarpenkoIA'), lowcase('admin'), lowcase('DubininYV'), lowcase('NikitinMD'), lowcase('demo'), lowcase('BalashovAN')) and!requesttodept.matchwild('*VIK*') then
        !mailuser = 'Korolkova'
       elseif!um1 inset(lowcase ('GoncharenkoEA'), lowcase('KarpenkoIA'), lowcase('admin'), lowcase('DubininYV'), lowcase('NikitinMD'), lowcase('demo'), lowcase('BalashovAN') ) and!requesttodept.matchwild('*TMO*') then
        !mailuser = 'denisov'
      endif
    endif


    if !dept1 neq!this.mydept and!dept2 neq !this.mydept then
      skip
    endif



  --id type E1 type1 usermod1 dept1 gpset1 E2 type2 usermod2 dept2 gpset2 date x y z existing RequestTo RequestUser RequestDate ApproveUser ApproveDate ApproveReason InWorkUser InWorkDate
  --1  2    3  4      5        6     7     8   9    10       11    12     13   14151617       18        19          20          21          22          23            24         25
      --если запрос не в мой отдел и поле approve пустое и поле inWork пустое
    if (!requesttodept neq!this.mydept and!sr[!i][21] eq '' and!sr[!i][24] eq '' and!sr[!i][19] neq '') then
      !umarray.append(!mailuser)
      !idarray.append(!id)
      !deptsarray.append(!requesttodept)
    endif
  enddo


  !uu = !umarray
  !uu.sortunique()
  !uucounter = array()

  !iduser = array()
  !iddept = ''
  do !i indices!uu
    !uucounter[!i] = 0
    do !j indices!umarray
      if !uu[!i] eq!umarray[!j] then
        !uucounter[!i] = !uucounter[!i] + 1
        !strokaid = !idarray[!j] + '<BR>'
        !iduser.append(!strokaid)
        !iddept = !deptsarray[!j]
      endif
    enddo


    if !iddept eq 'IMC' then
    !iddept = 'TMO'
    endif

    var !prj project code
    !adressee = object array()
    !Subject = 'Запрос на согласование коллизий по проекту $!prj комлекту $!this.currGpset'
    !MessageArr = array()
    !MessageArr.append('Прошу устранить или согласовать коллизии по комплекту $!this.currGpset в количестве $!uucounter[$!i] шт <BR>')
    !MessageArr.append('Номера коллизий: <BR>')
    !MessageArr.appendarray(!iduser)
    !!SendMail(!uu[!i] & '@tep-m.ru', !Subject, !MessageArr, object array())
    !adressee.Append( | !!SendMail( | &!uu[!i] & | '@tep-m.ru', !Subject, !MessageArr, object array() | )
    --доп отправка писем с расчётом на то что некоторые могут быть в отпуске
    if !iddept eq 'SOT' then
      !adresat = 'vinogradov'
      !!SendMail(!adresat & '@tep-m.ru', !Subject, !MessageArr, object array())
      !adresat = 'presnov'
      !!SendMail(!adresat & '@tep-m.ru', !Subject, !MessageArr, object array())
    elseif!iddept eq 'ARX' then

        if !prj eq 'TUY' then
           !adresat = 'kotova'
           !!SendMail(!adresat & '@tep-m.ru', !Subject, !MessageArr, object array())
        elseif!prj eq 'UYK' then
           !adresat = 'drachevai'
           !!SendMail(!adresat & '@tep-m.ru', !Subject, !MessageArr, object array())

        else
    !adresat = 'izmaylovaim'
    !!SendMail(!adresat & '@tep-m.ru', !Subject, !MessageArr, object array())
    !adresat = 'vnukovaya'
    !!SendMail(!adresat & '@tep-m.ru', !Subject, !MessageArr, object array())

        endif
    elseif !iddept eq 'OIV' then

        if !prj eq 'TUY' then
           !adresat = 'SukhorukovAY'
           !!SendMail(!adresat & '@tep-m.ru', !Subject, !MessageArr, object array())

        else
    !adresat = 'zolotov'
    !!SendMail(!adresat & '@tep-m.ru', !Subject, !MessageArr, object array())

        endif
    elseif !iddept eq 'OMK' then
      !adresat = 'lyanas'
      !!SendMail(!adresat & '@tep-m.ru', !Subject, !MessageArr, object array())
    elseif!iddept eq 'VIK' then
        !adresat = 'Korolkova'
        !!SendMail(!adresat & '@tep-m.ru', !Subject, !MessageArr, object array())
    endif
  enddo



  --отправка копии письма самому себе
  if !!Alert.Confirm('Отправить копию письма самому себе?').Boolean() then
  !!SendMail(!login.lowcase() & '@tep-m.ru', !Subject & ' ( to ' & !uu[$!i] & ' ) ', !MessageArr, object array())
  handle(2, 751)
  var!prj project code
  !Subject = 'Запрос на согласование коллизий по проекту $!prj'
  endhandle
  endif


  !e = ''


  if !uu.size() eq 0 then
    !msg = 'Нет запросов на согласование для отправки по E-mail' & !e
  else
    !msg = 'Уведомление по E-mail отправлено' & !e
    do !i indices!uu
      !msg = !msg & 'для $!uu[$!i] $!uucounter[$!i] шт' & !e
    enddo
  endif


  !!alert.message(!msg)
endmethod

define method.ResetStatusOfThisClash()
!sr = !this.TableD.getselectedrows()

  do !i from 1 to!sr.size()
    !id = !sr[!i][1]
	$P $!id
    !query = | update $!this.tn SET requesttodept = '', requestuser = '', requestdate = NULL, ApproveUser = '', ApproveDate = NULL, ApproveReason = '', InWorkUser = '', InWorkDate = NULL  WHERE id = '$!id' |
    !sqlarray = !!sqlQuery('SQL', !this.conn, !query)
  enddo
  !this.show()

endmethod


define method.sendrequest() $*из контекстного меню
  !dt = object datetime()
  !date = !this.format.string(!dt)

  !sr = !this.TableD.getselectedrows()

  if !sr.size() eq 0 then
    !!alert.message('не выбраны коллизии для отправки запроса')

    return
  endif


  do !i from 1 to!sr.size()
    !id = !sr[!i][1]
	$P $!id
    --отправляет запрос к тому кто не "мы"
    !dept1 = !sr[!i][6]
    !dept2 = !sr[!i][11]


    if !dept1 eq!this.mydept then
      !requesttodept = !dept2

    endif

    if !dept2 eq!this.mydept then
      !requesttodept = !dept1

    endif

    if !dept1 neq!this.mydept and!dept2 neq !this.mydept then
	  $P $!id - это коллизия других отделов($!dept1 и $!dept2)

      return
    endif


    if (!sr[!i][18] neq '' or!sr[!i][19] neq '' or!sr[!i][20] neq '') then
      !!alert.message('запрос уже отправлен (id=$!sr[$!i][1])')

      return
  endif


    if (!sr[!i][21] neq ''or!sr[!i][22] neq '' or!sr[!i][23] neq '') then
      !!alert.message('нельзя отправить запрос по уже согласованной коллизии (id=$!sr[$!i][1])')

      return
  endif
    --если коллизия не наша то отбой

    !query = | update $!this.tn SET requesttodept = '$!requesttodept', requestuser = '$!this.myUlogId', requestdate = '$!date' WHERE id = '$!id' |
    !sqlarray = !!sqlQuery('SQL', !this.conn, !query)
  enddo
  !this.show()
endmethod


--пробуем сдать комплект(тут делается проверка можно ли сдавать)
define method .trytopass()
  !gpset = !this.currgpset
  !type = ''
  !type = type of $!gpset
  handle any
  endhandle


  if !type neq 'GPSET' then
    !!alert.message('$!gpset не является комплектом. сдавать можно только комлект')
    return
  endif

  --если комплект чужого отдел, то отбой
  --если отделы не совпали и это не сочетание отдел = ОГС и комплект = СОТ
  if !!GetDepartment(!gpset.dbref(), 'GPSET') neq!this.myDept and not(!!GetDepartment(!gpset.dbref(), 'GPSET') eq 'SOT' and!this.myDept eq 'OGS' ) then

      if (!!alert.confirm(| Вы не можете сдавать комплект другого отдела!(NO - продолжить) |) neq | NO |) then

            return
        endif
  endif

  -- if :UES_KSLOCK of $!gpset eq 10 then
  --!!alert.message('$!gpset уже был заблокирован ранее. Не надо сдавать уже сданный комплект.')
  --   return
  --endif

  -- TeplykhAK 20210526 start--
  -- Исправлен запрос для подсчета принятых внутри отдела коллизий
  --!query = | select  count(*) from $!this.tn where(gpset1 = '$!gpset' or gpset2 = '$!gpset') and(approveReason is null or approveReason = '') and not(requestToDept<> '$!this.mydept' and(InWorkUser is not null))|
  --!query = | select  count(*) from $!this.tn where(gpset1 = '$!gpset' or gpset2 = '$!gpset') and(approveReason is null or approveReason = '') and(requestToDept<> '$!this.mydept' or(InWorkUser is null)) |
  !query = | select  count(*) from $!this.tn where(gpset1 = '$!gpset' or gpset2 = '$!gpset') and(approveReason is null or approveReason = '') and(InWorkUser is null) |
  --TeplykhAK 20210526 end--

  --здесь надо написать что при условии что комплект сдаваемый сегодня не менялся, то исключить из подсчёта коллизии которые появились сегодня
  !gpsetlastmod = !this.getgpsetLastmod(!gpset)
  !now = object datetime() $*текущее время
  !todaymidnight = object datetime(!now.year(), !now.month(), !now.date(), 0, 0) $*полночь
  !todaymidnightSTR = !now.month() & '.' & !now.date() & '.' & !now.year() $*строка в формате сервера(месяц сначала)
  if !gpsetlastmod.lt(!todaymidnight) then  $*если комплект сегодня не менялся
  $P $!todaymidnightSTR
    --то из подсчёта исключить коллизии с сегодняшней датой обнаружения | and date < '12.25.2013' |

    --TeplykhAK 20210526 start--
    -- Исправлен запрос для подсчета принятых внутри отдела коллизий
    --!query = | select  count(*) from $!this.tn where(gpset1 = '$!gpset' or gpset2 = '$!gpset') and(approveReason is null or approveReason = '') and not(requestToDept<> '$!this.mydept' and(InWorkUser is not null)) and date< '$!todaymidnightSTR' |
    --!query = | select  count(*) from $!this.tn where(gpset1 = '$!gpset' or gpset2 = '$!gpset') and(approveReason is null or approveReason = '') and(requestToDept<> '$!this.mydept' or(InWorkUser is null)) and date< '$!todaymidnightSTR' |
    !query = | select  count(*) from $!this.tn where(gpset1 = '$!gpset' or gpset2 = '$!gpset') and(approveReason is null or approveReason = '') and(InWorkUser is null) and date< '$!todaymidnightSTR' |
  --TeplykhAK 20210526 end--
  endif

  !sqlarray = !!sqlQuery('SQL', !this.conn, !query)
  !ss = !sqlarray[0][0]
  $P несогласованных коллизий этого комплекта $!ss шт
  if $!ss eq 0 then

    if !this.isGreengpset(!gpset) then
      !this.report()
    else
    !!alert.message('хотя в базе по данному комплекту несогласованных коллизий не обнаружено. комплект должен быть проверен непосредственно пред сдачей, т.к. могут появятся новые коллизии')
    endif
  else
    !!alert.message('несогласованных коллизий этого комплекта ($!gpset) $!ss шт')
  endif
endmethod

--данный метод сдаёт комплект безусловно

define method .report()
    !gpset = !this.currgpset
    !!LockKomplect(!gpset.dbref())


        var!project project code

        !Subject = 'Авто: Сдан (заблокирован) комплект $!gpSet (Проект $!PROJECT ).'
        !Message = 'Заблокирован комплект (в PDMS и TDMS) ' + !gpSet + '.'
        !messageARR = array()
        !messageARR.append(!message)

        --!!SendMail('sapr@tep-m.ru', !Subject, !MessageArr, object array())

        !!alert.Message('Комплект успешно сдан и заблокирован')


endmethod

define method.reportold()
    !gpset = !this.currgpset
	$!gpset

        handle ANY
			$P Ничего не выбрано!

            return
        endhandle

    var!Status: UES_STATUS

    !Answer = 'YES'

    --если комплект не 10(то есть не сдан)

    if Real(!Status) NEQ 10 then

        !Session = CURRENT SESSION
        !Login = !Session.Login
        !DT = object DATETIME()

        !DF = object DATEFORMAT('D-M-Y T')
        !Date = !DF.String(!DT)

        --получаем список элементов комплекта

        !SetMem = !!CE.mem
        !ExtractSitem = object BLOCK('!SetMem[!EvalIndex].Sitem')
        !SitemName = !SetMem.Evaluate(!ExtractSitem)

        --бежим по элементам комплекта проставляем 10 и сейвим базу этого элемента

        do !x values!SitemName
			$P $!x
			$!x
            !DbNameEl = !!CE.Attribute('DBNAME')
			
			:UES_STATUS 10

            handle(2, 502)

            elsehandle(41, 232)
			$P Элеменет $P $!x не имеет атрибута: UES_STATUS
            endhandle

            savework $!DbNameEl

            unclaim all

        enddo

        --становимся на комплект проставляем атрибуты и сейвим базу комплектов
		$!gpset
		
		:UES_ARX 'Нет'

        HANDLE ANY

         !!Alert.error('Комплект не заблокирован! Срочно обратитесь в ОАП')

        ELSEHANDLE NONE
		:UES_ASU 'Нет'
		
		:UES_ETO 'Нет'
		:UES_OGS 'Нет'
		:UES_OIV 'Нет'
		:UES_OMK 'Нет'
		:UES_OVS 'Нет'
		:UES_OWP 'Нет'
		:UES_TMO 'Нет'
		:UES_SOT 'Нет'
		:UES_VIK 'Нет'
		:UES_USER '$!Login $!Date'

        !Revision = :UES_REVISION
        !Revision = !Revision + 1
		:UES_REVISION $!Revision
		
		:UES_STATUS 10

        !gpsetdbname = !!CE.Attribute('DBNAME')

        savework $!gpsetdbname

        unclaim all


        ENDHANDLE

        -- -
        --здесь был код обновления статуса комплекта в базе
        -- -

        --по умолчанию письмо в ОАП шлётся
        !Answer = 'YES'

    else
    --но если комплект уже был заблокирован.надо переспросить хочешь ещё отправить?

        !Answer = !!Alert.Question('Комплект уже сдан. Вы хотите отправить сообщение еще раз?')

    endif

    -- Отправка письма в ОАП


    if !Answer EQ 'YES' then
        var !project project code


        !Revision = :UES_REVISION
        !Subject = 'Авто: Разблокировка комплекта $!gpSet ревизия $!Revision (Проект $!PROJECT ).'
        !Message = 'Прошу разблокировать комплект ' + !gpSet + '.'
        !messageARR = array()
        !messageARR.append(!message)

        !!SendMail('sapr@tep-m.ru', !Subject, !MessageArr, object array())

        !!alert.Message('Сообщение отправлено!')

    endif

endmethod

define method .TakeInWork() $*из контекстного меню
  --клеш можно принять в работу если он не согласован и есть запрос(апрув пустой и реквест мне)
  !dt = object datetime()
  !date = !this.format.string(!dt)

  !sr = !this.TableD.getselectedrows()
  if !sr.size() eq 0 then
    !!alert.message('не выбраны коллизии для принятия в работу')

    return
  endif

  --сначала убеждаемся что все можно взять в работу
  do !i from 1 to!sr.size()
    --если нет запроса, то отбой
    if (!sr[!i][18] neq!this.mydept ) then
      !!alert.message('взять в работу можно только колиизии по которым есть запрос в ваш отдел (колонка RequestTo)')
      return
    endif

    --если уже согласована, то отбой
    if (!sr[!i][21] neq '' or!sr[!i][22] neq '' or!sr[!i][23] neq '') then
      !!alert.message('нельзя брать в работу уже согласованную коллизию (id=$!sr[$!i][1])')
      return
    endif
    if (!sr[!i][24] neq '' or!sr[!i][25] neq '' ) then
      !!alert.message('коллизия уже прията в работу(id=$!sr[$!i][1])')
      return
    endif
  enddo


  do !i from 1 to!sr.size()
    !id = !sr[!i][1]
	$P принимается в работу $!id
    --если это коллизия с запросом(не пустые поля реквест) и не согласованная(пустые поля апрув) то апдейтим поля InWork

    if (!sr[!i][18] eq!this.mydept and!sr[!i][19] neq '' and!sr[!i][20] neq '' and!sr[!i][21] eq '' or!sr[!i][22] eq '' or!sr[!i][23] eq '' ) then
      !query = | update $!this.tn SET InWorkUser = '$!this.myUlogId', InWorkDate = '$!date' WHERE id = '$!id' |

    else
    !!alert.message('сбой при принятии в работу коллизий. обратитесь в ОАП')

      return
    endif

    !sqlarray = !!sqlQuery('SQL', !this.conn, !query)
  enddo

  --если после этого нет ни одной коллизии по этому комплекту этого отдела(все согласовали) то сообщение
  !gpset = !this.SelSet.Selection()
  if !gpset eq 'ALL' or!gpset eq 'CE' then
    return
  endif
  !query = | select  count(*) from $!this.tn where(gpset1 = '$!gpset' or gpset2 = '$!gpset') and(dept1 = '$!this.MYDEPT' or dept2 = '$!this.MYDEPT') and(approveReason is null or approveReason = '') |

  !sqlarray = !!sqlQuery('SQL', !this.conn, !query)
  !ss = !sqlarray[0][0]
  $P несогласованных коллизий этого комплекта моего отдела $!ss шт
  if $!ss eq 0 then
    !!alert.message('Поздравляю! согласованы все коллизии для комплекта $!gpset относящиеся к отделу $!this.MYDEPT')
  endif

  !this.updateGpsetList()
  !this.selSet.select('Rtext', !this.currGpset)
  !this.show()


endmethod


define method.ApproveClash() $*из контекстного меню
  --клеш можно согласовать при условии что твоему отделу отправлен запрос(или что он моего отдела с моим)
  --и нельзя если взят мной в работу!!
  !dt = object datetime()
  !date = !this.format.string(!dt)

  !sr = !this.TableD.getselectedrows()
    if !sr.size() eq 0 then
    !!alert.message('не выбраны коллизии для согласования')

    return
  endif

  --сначала убеждаемся что по всем коллизиям есть запрос в мой отдел иначе отбой
  --id type E1 type1 usermod1 dept1 gpset1 E2 type2 usermod2 dept2 gpset2 date x y z existing RequestTo RequestUser RequestDate ApproveUser ApproveDate ApproveReason InWorkUser InWorkDate
  --1  2    3  4      5        6     7     8   9    10       11    12     13   14151617       18        19          20          21          22          23            24         25
  do !i from 1 to!sr.size()
    --если нет запроса и если не наш отдел
     if (!sr[!i][18] neq!this.myDept and not(!sr[!i][6] eq!this.myDept and!sr[!i][11] eq!this.myDept) ) then
      !!alert.message('согласовать можно только колиизии по которым есть запрос в ваш отдел (колонка RequestTo) или коллизии внутри отдела')
      return
    endif


    if (!sr[!i][21] neq ''or!sr[!i][22] neq '' or!sr[!i][23] neq '') then
      !!alert.message('нельзя согласовать уже согласованную коллизию (id=$!sr[$!i][1])')
      return
    endif
    if (!sr[!i][24] neq '' or!sr[!i][25] neq '' ) then
      !!alert.message('нельзя согласовать приятую в работу коллизию(id=$!sr[$!i][1])')
      return
    endif
  enddo

  --спрашиваем причину согласования
  !ApproveReason = !!alert.input('Введите причину согласования', '')
  if !ApproveReason.replace(' ', '').length() lt 5 then
    !!alert.message('Согласование отменено. Причина согласования не может быть менее 5 символов')
    return
  endif


  do !i from 1 to!sr.size()
    !id = !sr[!i][1]
	$P согласуется $!id
    --если это коллизия с запросом(не пустые поля реквест) то апдейтим тока часть про согласование

    if (!sr[!i][18] neq '' and!sr[!i][19] neq '' and!sr[!i][20] neq '' ) then
      !query = | update $!this.tn SET ApproveUser = '$!this.myUlogId', ApproveDate = '$!date', ApproveReason = '$!ApproveReason' WHERE id = '$!id' |
      --иначе если без запроса(тоесть внутри отдела) и внутри отдела, то сразу и шлём запрос и апрувим самому себе

    elseif(!sr[!i][18] eq '' and!sr[!i][19] eq '' and!sr[!i][20] eq '' and(!sr[!i][6] eq!this.myDept and!sr[!i][11] eq!this.myDept)) then
      --тут в одном запросе как бы два и запрос и согласование коллизии

      !query = | update $!this.tn SET requesttodept = '$!this.myDept', requestuser = '$!this.myUlogId', requestdate = '$!date', ApproveUser = '$!this.myUlogId', ApproveDate = '$!date', ApproveReason = '$!ApproveReason' WHERE id = '$!id' |

    else
    q var !sr[!i][20]
      !!alert.message('сбой при согласовании коллизий. обратитесь в ОАП')


      return
    endif

    !sqlarray = !!sqlQuery('SQL', !this.conn, !query)
  enddo

  --если после этого нет ни одной коллизии по этому комплекту этого отдела(все согласовали) то сообщение
  !gpset = !this.SelSet.Selection()
  if !gpset eq 'ALL' or!gpset eq 'CE' then
    return
  endif
  !query = | select  count(*) from $!this.tn where(gpset1 = '$!gpset' or gpset2 = '$!gpset') and(dept1 = '$!this.MYDEPT' or dept2 = '$!this.MYDEPT') and(approveReason is null or approveReason = '') |

  !sqlarray = !!sqlQuery('SQL', !this.conn, !query)
  !ss = !sqlarray[0][0]
  $P несогласованных коллизий этого комплекта моего отдела $!ss шт
  if $!ss eq 0 then
    !!alert.message('Поздравляю! согласованы все коллизии для комплекта $!gpset относящиеся к отделу $!this.MYDEPT')
  endif

  !this.updateGpsetList()
  !this.selSet.select('Rtext', !this.currGpset)
  !this.show()


endmethod

define method.gotoel1() $*из контекстного меню
  !el1 = !this.TableD.getselectedrows()[1][3]
  $!el1
endmethod

define method.gotoel2() $*из контекстного меню
  !el2 = !this.TableD.getselectedrows()[1][8]
  $!el2
endmethod

define method.popupMeth(!passedArray is ARRAY)
  !this.conTableDif.popup = !this.popupMeth
  !this.conTableDif.showPopup(!passedArray[0], !passedArray[1])
endmethod


define method.CheckByOldBase()
    --нулевую дату получаем заранее

    !dt = object datetime(1990, 1, 1)
    !date = !this.format.string(!dt)

!sr = !this.TableD.getselectedrows()

  do !i from 1 to!sr.size()
    !id = !sr[!i][1]
    !e1 = !sr[!i][3]
    !e2 = !sr[!i][8]
    !res = !!QueryOneClash(!e1, !e2)
    !approveuser = ''
    !approvereason = ''

    if !res.size() eq 0 then
	  $p  $!id не найдена

    elseif!res.size() ge 1 then



      do !j from 0 to!res.size() - 1

        if !res[!j].split('`')[6] eq '0' and!res[!j].split('`')[7] eq '0' then
          !approveuser = !res[!j].split('`')[10]
          !approvereason = !res[!j].split('`')[9]
          !query = | update $!this.tn SET RequestToDept = 'SYS', RequestUser = 'pdmsadmin', requestDate = '$!date', ApproveUser = '$!approveuser', ApproveDate = '$!date', ApproveReason = '$!ApproveReason' WHERE id = '$!id' |
          !sqlarray = !!sqlQuery('SQL', !this.conn, !query)

            break

        endif
      enddo $*цикл по найденным в старой базе
    endif
  enddo

endmethod

define method .CheckExistElement()
  !sr = !this.TableD.getselectedrows()
  do !i from 1 to!sr.size()
    !checkEL = !sr[!i][3]
    !ObstEL = !sr[!i][8]
    if !!IsNeedToDeleteClash(!checkEL, !ObstEL) then
      $P $!sr[$!i][1] $!checkEL $!ObstEL!needtodelete = true
    else
      $P $!sr[$!i][1] $!checkEL $!ObstEL!needtodelete = false
    endif
  enddo
endmethod

define method.CheckThisClash()

  !sr = !this.TableD.getselectedrows()


  DESCLASH
  REP OBSTR OFF

  REPORT MAIN OFF
  REPORT SIGNIFICANT OFF
  REPORT SUMMARY OFF


  --begin of TEP
  OVERRIDE ON
  MIDPOINT ON
  REPORT POSITION WORLD
  TOUCH GAP OFF
  TOUCH OVER 2
  CLEARANCE OFF
  IGNORE TOUCHES
  BRANC B
  IGNORE CONNECTIONS
  NOCHECK WITHIN EQUI
  NOCHECK WITHIN STRU
  NOCHECK WITHIN BRAN
  NOCHECK WITHIN REST
  --end of TEP

  do !i from 1 to!sr.size()
    !checkEL = !sr[!i][3]
    !ObstEL = !sr[!i][8]
    REM OBST ALL
    OBST $!ObstEL
    CHECK $!checkEL


    VAR!allClashes CLASH COUNT CLASHES

    var!el1 CLASH $!i FIRST
    var !el2 CLASH $!i SECOND
    var !type CLASH $!i TYPE
    var !pos CLASH $!i POSITION
    $P $!sr[$!i][1] $!allClashes $!el1 $!el2 $!type $!pos
  enddo
EXIT


endmethod

define method.checkGPSET(!gpsetname is string)
-- q var !gpsetname
--var!user user
--if !user neq 'SYSTEM' then

  var !Project Project code
  var!mdb MDB
  !isAll = !mdb.eq(!Project & |.ALL |)


    if !mdb NEQ '/ALL' and!mdb NEQ '/16310' and!isAll then

        !Answer = !!Alert.Confirm('Проверку комплекта необходимо запускать в MDB "ALL". Сохраниться и перейти в MDB "ALL"?')

        if !Answer EQ 'YES' then
            savework

            mdb noup

            / ALL

            exit

        else
    return
  endif

    endif
--endif

  !sessionData = CURRENT SESSION
  if !sessionData.modified() then
    --надо бы сохраниться!!
    !Answer = !!Alert.Confirm('Перед началом проверки необходимо сохраниться.  SaveWork ?')
    if !Answer EQ 'YES' then
      savework
    else
    return
endif
  endif
  --getwork надо делать для исключения ситуации в которой updateclashelementinfo затирает(портит информацию по клешам о принадлежности к комплекту и не только...)
  --если save делатся при наичии изменений(!sessionData.modified()), то getwork делается всегда
  getwork

  --перед началом убеждаемся что нет коллизий existing = false
  !query = | select  count(*) from $!this.tn where(existing = 'false') and(gpset1 = '$!gpsetname' or gpset2 = '$!gpsetname') |
  !sqlarray = !!sqlQuery('SQL', !this.conn, !query)
  !notexistcount = !sqlarray[0][0]
  if !notexistcount neq '0' then
    -- $P!notexistcount - $!notexistcount
    --!query = | update[pdms].[dbo].[$!this.tn] set existing = 1 where existing = 0 and(gpset1 = '$!gpsetname' or gpset2 = '$!gpsetname') |
    --!!sqlQuery('SQL', !conn, !query)
    !!alert.message('действие отменено. проверка невозможна. обратитесь в ОАП')
    return
  endif

  --этот метод нужен потому что нужно удалять несуществующие коллизии какого-то компекта
  --обновляем информацию потомучто те коллизии которые в SQL относятся к этому комплекту, а на самом деле выпали из него или перемещены в другой комплект удалятся. это не есть хорошо.
  --алгоритм такой
  --1 апдейтим инфу

    !gpset = !gpsetname.dbref()

    handle any

        !!alert.message('Выбранный комплект $!gpsetname - не существует')

        return
    endhandle
  if !gpset.type eq 'GPSET' then
    var !login login
    !!UpdateClashElementInfo('', !gpsetname)
  endif
  !Now = object DATETIME()
  $P $!Now завершено обновление информации
    --собираем в массив id коллизий сомнительных

    if type of $!gpsetname eq 'GPSET' then
      !query = | select id from $!this.tn WHERE(gpset1 = '$!gpsetname' or gpset2 = '$!gpsetname') |
      !sqlarray = !!sqlQuery('SQL', !this.conn, !query)
      !clashcount = !sqlarray.size()

    else
    !sqlarray = !!QueryClashByEl(!gpsetname.dbref(), '')
    !clashcount = !sqlarray.size()

    endif
  !!MayBeDeleteIDArray = array()
  !!MayBeDeleteIDArray.clear()
  !!existingArray = array()
  !!existingArray.clear()
  do !i from 0 to!sqlarray.size() - 1
    !!MayBeDeleteIDArray.append(!sqlarray[!i][0])
    !!existingArray.append(false)
  enddo
q var!!MayBeDeleteIDArray
  $P $!Now коллизии комплекта поставлены под сомнение
  $P коллизий элмента $!gpsetname до проверки $!clashcount

!checkEL = !gpsetname
/*
var!proje proj id
if !proje inset('GCC', 'TUY') then
  var !colzone collect all zone with(not matchw(name, '*PO*') and not matchw(name, '*STUDY*') and not matchw(name of site, '*ZEMI*') and matchw(name of site, '??'))
/*else
    var!colzone collect all zone with((not matchw(name of site, '*.L') and purp of site neq 'AXES' and purp of site neq 'NOCL' and mcount neq 0) or matchw(name,'*CLASH*'))
endif
!s = !colzone.size()
$P всего зон $!s шт
var !wvolarray evaluate wvol for all from !colzone IGNORE ANY ||
var!wvol wvol of $!checkEL

syscom | mkdir d:\temp |

FILE / d:\temp\clash - gpset.txt OVER
DESCLASH
REP OBSTR OFF

  REPORT SUMMARY OFF

  --begin of TEP
  OVERRIDE ON
  MIDPOINT ON
  REPORT POSITION WORLD
  TOUCH GAP OFF
  TOUCH OVER 2
  CLEARANCE OFF
  IGNORE TOUCHES
  BRANC B
  IGNORE CONNECTIONS
  NOCHECK WITHIN EQUI
  NOCHECK WITHIN STRU
  NOCHECK WITHIN BRAN
  NOCHECK WITHIN REST
  --NOCHECK WITHIN VOLM
  --end of TEP


  REM OBST ALL
  !obstzonecount = 0
  do !J from 1 to!colzone.size()


    if !wvolarray[!J].split().size() eq 6 and!wvol.split().size() eq 6 then
    --$p я тут
      if !!wvolClash(!wvolarray[!J].split(), !wvol.split()) eq 1 then
        --$p и тут тоже
        OBST $!colzone[$!J]
        !obstzonecount = !obstzonecount + 1
      endif
    endif
  enddo $*конец цикла зонам для OBST листа
  $P!obstzonecount = $!obstzonecount
--выбрать элемент
var!ob clash obst
handle ANY
  !ob = ' error var !ob clash obst'
  --$P $!ob
endhandle
if !ob neq 'None' then
 CHECK $!checkEL
endif
FILE END

  !Now = object DATETIME()
  $P $!Now завершена проверка комплекта
  !!checkresulttoBase()
  !Now = object DATETIME()
  $P $!Now завершено обновление данных о коллизиях в базе
EXIT
--------------------------------------------------------------------------------
  !query = |select  count (*) from $!this.tn where (gpset1 = '$!gpsetname' or gpset2 = '$!gpsetname')|
  q var !gpsetname
  !sqlarray = !!sqlQuery('SQL',!this.conn,!query)
  !clashcount = real(!sqlarray[0][0])
  $P коллизий комплекта $!gpsetname после проверки $!clashcount
  
  --здесь очищается из базы коллизии с тем ID, который присутствует в MayBeDeleteIDArray с неподтверждённым существованием !!existingArray, т.е. не отработала !!resurrectclash() вызываемая из InsertOneClash
  
  --так делать некорректно, т.к. возможно элементы просто были убраны из комплекта и из-за этого коллизия будет считаться не обнаруженной,
  --на самом деле она есть просто в другом комплекте, именно из этих соображений делалось обновление инфы (раньше, которое занимет много времени если обновлять всю базу)
  --правильным будет здесь у неподтверждённых коллизий апдейтить инфу принажлежности к комплекту и оставлять в базе коллизию
  do !i indices !!MayBeDeleteIDArray
    if (!!existingArray[!i]) eq false then
	  !tmpid = !!MayBeDeleteIDArray[!i]
	  
	  !!DeleteById(!this.conn,!this.tn,!tmpid,'AfterCheckGPSET','.checkGPSET коллизия не относится к комплекту. удалена по завершению проверки')

	  $P удалена как несуществующая $!tmpid
		--10.12.2025 Калинин Г.Д. Закомментировал логирование как вероятную причину фатала
	  -- !str = '$!now $!this.MyUlogId проверялся $!gpsetname удален(не подтвердился) $!tmpid'
	  -- q var !str
	   -- q var !this.CLASHdir
	  -- !!AppendToFile(!str,!this.CLASHdir + '\' + 'CheckGpset-log.txt')
    endif
  enddo
  
  !query = |select  count (*) from $!this.tn where (gpset1 = '$!gpsetname' or gpset2 = '$!gpsetname')|
  !sqlarray = !!sqlQuery('SQL',!this.conn,!query)
  !clashcount = real(!sqlarray[0][0])
  $P коллизий комплекта $!gpsetname после удаления несуществующих $!clashcount
      
  !this.updateGpsetList()
  !this.selSet.select('Rtext',!gpsetname)
  !this.show()
  --массив чекнутых пополняем только если это был комплект
  if type of $!gpsetname eq 'GPSET' then
    !finded = false
    --пытаемся обновить время проверки комплекта
    do !i from 1 to !this.checkedsets.size()
      if !this.checkedsets[!i] eq !gpsetname then
        !this.checkedsetsTime[!i] = object datetime()
        break
      endif
    enddo
    --если не найден, то добавляем
    if not !finded then
      !this.checkedsets.append(!gpsetname)
	  !this.checkedsetsTIME.append( object datetime())
    endif
  endif
  !this.UpdateCheckedStatus()
endmethod

define method .ExportToExcel () 
 $P method .ExportToExcel ()
 
   !this.TableD.SaveGridToExcel('D:\' & !this.currgpset.Substring(2) & '.xlsx' )
   
 
endmethod
    /*