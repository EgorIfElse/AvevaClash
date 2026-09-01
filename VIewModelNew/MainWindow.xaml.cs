using Aveva.ClashChecker.NetCallable;
using Aveva.ClashChecker.NetCallable.Models;
using Aveva.Core.Database;
using ClashViewForm;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VIewModel;
using static System.Windows.Forms.AxHost;
using Brushes = System.Windows.Media.Brushes;
using CC = global::ClashChecker.ClashChecker;
using PML = Aveva.Core.Utilities.CommandLine.Command;


namespace ViewForm;

/// <summary>
/// Логика взаимодействия для MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _isRefreshing;
    public ClashViewForm.ClashViewForm logic = new ClashViewForm.ClashViewForm();
    public CC clash = new CC();
    public string CurrZone = "";
    public string ClashTableName = "";
    public string ProjectName = "";
    public string MyDept = "";
    public HashSet<string> MyDepartments { get; private set; } =
    new(StringComparer.OrdinalIgnoreCase);
    public string MyUlogId = "";
    public string ClashConnectionString = "";
    private const string DefaultLogDirectoryPath = "C:\\AVEVA\\ClasherLogs\\ClashLog.log";
    private ClashLogger Logger { get; set; } = new ClashLogger(DefaultLogDirectoryPath);
    public MainWindow()
    {
        InitializeComponent();
       
       
        LoadZone();
      
        MyDept = logic.MyDept;
        MyDepartments = logic.MyDepartments;
        MyUlogId = logic.MyUlogId;
        ProjectName = Project.CurrentProject.Name;
        ClashTableName = $"clashtable{ProjectName}_TEST";
        ClashConnectionString = logic.ClashConnectionString;
        //string selectedZone = CbZone.SelectedValue.ToString();
        CurrZone = "";
    }

    private bool HasDepartmentAccess(string department)
    {
        if (MyDept == "AB")
        {
            return true;
        }
        return !string.IsNullOrWhiteSpace(department) && MyDepartments.Contains(department);
    }




    private void Notify_Onclick(object sender, RoutedEventArgs e)
    {
        
        
    }
    public void SendMailByRequest2(List<ClashEntity> rows)
    {
        
            string project = Project.CurrentProject.Name;
            var mailDict = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (ClashEntity clash in rows)
        {
        

                string mailuser = clash.SecondUserMode;
                if(string.IsNullOrWhiteSpace(mailuser))
            {
                Logger.WriteLine($"У коллизии {clash.Id} не заполнен U2. Письмо не отправлено");
                continue;
            }

                if (!mailDict.ContainsKey(mailuser))
                    mailDict[mailuser] = new List<int>();
                mailDict[mailuser].Add(clash.Id);
        }

            var sentUsers = new List<string>();
            var failedUsers = new List<string>();

            // отправка
            foreach (var kvp in mailDict)
            {
                string user = kvp.Key;
                List<int> ids = kvp.Value;
                string subject = $"Запрос на согласование коллизий по проекту {project}, зона {CurrZone}";
                string body = $"Прошу устранить или согласовать коллизии по зоне {CurrZone} в количестве {ids.Count} шт. <BR>"
                               + "Номера коллизий: <BR>"
                               + string.Join("<BR>", ids);

                string userMail = GetUserMail(user);
                if (string.IsNullOrWhiteSpace(userMail))
                {
                    Logger.WriteLine(
                        $"У пользователя {user} не заполнен атрибут :UserMail. Письмо не отправлено.");
                    failedUsers.Add(user);
                    continue;
                }

                if (SendMail(userMail, subject, body))
                    sentUsers.Add(user);
                else
                    failedUsers.Add(user);

                /*
                var clashRow = rows.FirstOrDefault(r => r.RequestUser == user);
                if (clashRow != null)
                    SendCcByDept(clashRow.RequestToDept ?? "", subject, body, project);
                */
            }

            string msg;
            if (mailDict.Count == 0)
                msg = "Нет запросов для отправки";
            else if (failedUsers.Count == 0)
                msg = "Уведомления отправлены: " + string.Join(", ", sentUsers);
            else if (sentUsers.Count == 0)
                msg = "Не удалось отправить уведомления: " + string.Join(", ", failedUsers);
            else
                msg = "Уведомления отправлены: " + string.Join(", ", sentUsers)
                      + "\nНе отправлены: " + string.Join(", ", failedUsers);



            MessageBox.Show(msg);
            
        

    }

    /*
    private void SendCcByDept(string dept, string subject, string body, string project)
    {
        List<string> cc = [];

        if (dept.Contains("SOT")) cc = ["vinogradov", "presnov"];
        else if (dept.Contains("ARX") && project == "TUY") cc = ["kotova"];
        else if (dept.Contains("ARX") && project == "UYK") cc = ["drachevai"];
        else if (dept.Contains("ARX")) cc = ["izmaylovaim", "vnukovaya"];
        else if (dept.Contains("OIV") && project == "TUY") cc = ["SukhorukovAY"];
        else if (dept.Contains("OIV")) cc = ["zolotov"];
        else if (dept.Contains("OMK")) cc = ["lyanas"];
        else if (dept.Contains("VIK")) cc = ["Korolkova"];

        foreach (var c in cc)
            SendMail($"{c}@k-pei.ru", subject, body);
    }
    */

    private string GetUserMail(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return string.Empty;

        try
        {
            DbElement ulog = DbElement.GetElement($"/+{userName}");
            DbAttribute userMailAttribute = DbAttribute.GetDbAttribute(":UserMail");

            return ulog.GetAsString(userMailAttribute)?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Logger.WriteLine(
                $"Не удалось получить :UserMail пользователя {userName}: {ex.Message}");
            return string.Empty;
        }
    }

    private bool SendMail(string to, string subject, string body)
    {
        try
        {
            string from = GetUserMail(MyUlogId);
            if (string.IsNullOrWhiteSpace(from))
            {
                Logger.WriteLine(
                    $"У текущего пользователя {MyUlogId} не заполнен атрибут :UserMail. Письмо не отправлено.");
                return false;
            }

            using var message = new MailMessage(from, to, subject, body)
            {
                IsBodyHtml = true,
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8
            };
            using var smtp = new SmtpClient("mail", 25)
            {
                EnableSsl = false,
                UseDefaultCredentials = false
            };
            smtp.Send(message);
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Ошибка отправки на {to}: {ex.Message}");
            return false;
        }
    }

    private void LoadZone()
    {
        string currentSelected = CurrZone;
       

            var zoneItems = logic.UpdateZoneList();
            CbZone.ItemsSource = zoneItems;
  
            if (!string.IsNullOrWhiteSpace(currentSelected))
            {
                CbZone.SelectedValue = currentSelected;
            }
    }
    //private bool ClashUsermod1Filter(object item)
    //{
    //    if (item is not ClashEntity clash)
    //        return false;
    //    if (string.IsNullOrWhiteSpace(TbUsermodFilter.Text))
    //        return true;
    //    return clash.FirstUserMode != null && clash.FirstUserMode.IndexOf(TbUsermodFilter.Text, StringComparison.OrdinalIgnoreCase) >= 0;
    //}
    private void CbZone_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {

        if (CbZone.SelectedValue == null)
            return;
        else
        {
            string selectedZone = CbZone.SelectedValue.ToString();
            CurrZone = selectedZone;
            UpdateZoneInfo();
        }
       

            Refresh();
       
    }

    private void UpdateZoneInfo()
    {
        TxtLastCheck.Text = "—";
        TxtDesigner.Text = "—";
        SetLastCheckColor(0x64, 0x74, 0x8B);

        if (string.IsNullOrWhiteSpace(CurrZone)
            || CurrZone == "ALL"
            || CurrZone == "CE")
        {
            return;
        }

        DbElement zone = DbElement.GetElement(CurrZone);
        if (zone.IsNull || !zone.IsValid)
            return;

        try
        {
            string designer = zone.GetAsString(DbAttribute.GetDbAttribute(":Designer"));

            if (!string.IsNullOrWhiteSpace(designer))
                TxtDesigner.Text = designer;
        }
        catch (Exception ex)
        {
            Logger.WriteLine(
                $"Не удалось получить :Designer зоны {CurrZone}: {ex.Message}");
        }

        try
        {
            DateTime lastCheck = logic.GetZoneLastCheck(CurrZone);

            if (lastCheck != DateTime.MinValue)
            {
                TxtLastCheck.Text = $"{lastCheck:dd.MM.yyyy HH:mm}";

                DateTime lastModified = logic.GetZoneLastModified(CurrZone);
                bool isActual = lastCheck >= lastModified
                    && (DateTime.Now - lastCheck).TotalDays <= 2;

                if (isActual)
                    SetLastCheckColor(0x3B, 0x82, 0xF6);
                else
                    SetLastCheckColor(0xEF, 0x44, 0x44);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine(
                $"Не удалось получить :Check зоны {CurrZone}: {ex.Message}");
        }
    }

    private void SetLastCheckColor(byte red, byte green, byte blue)
    {
        var brush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(red, green, blue));

        LastCheckIcon.Foreground = brush;
        TxtLastCheck.Foreground = brush;
    }

    //  private void LoadClashEntity(string zoneRef)
    //{
    // Refresh();

    //}
    private void DataGridRow_MouseRightButtonDown(object sender, RoutedEventArgs e)
    {
        DataGridRow row = sender as DataGridRow;
        row.IsSelected = true;
    }
    private void Search1_MouseRightButtonDown(object sender, RoutedEventArgs e)
    {
        if (DgClashes.SelectedItem == null) return;
        ClashEntity selectedClash = (ClashEntity)DgClashes.SelectedItem;
        string Search1 = selectedClash.FirstElement;

        var dbElement = DbElement.GetElement(Search1);

        if (dbElement.IsNull)
        {
            System.Windows.MessageBox.Show($"Элемент {Search1} не найден");
            return;

        }
        PML.CreateCommand($"CE {Search1}").RunInPdms();
    }
    private void Search2_MouseRightButtonDown(object sender, RoutedEventArgs e)
    {
        if (DgClashes.SelectedItem == null) return;
        ClashEntity selectedClash = (ClashEntity)DgClashes.SelectedItem;
        string Search2 = selectedClash.SecondElement;
        var dbElement = DbElement.GetElement(Search2);

        if (dbElement.IsNull)
        {
            System.Windows.MessageBox.Show($"Элемент {Search2} не найден");
            return;

        }
        PML.CreateCommand($"CE {Search2}").RunInPdms();


    }
    private void NumbersOfSelectedClash(object sender, RoutedEventArgs e)
    {
        int count = DgClashes.SelectedItems.Count;
        if (count == 0)
        {
            System.Windows.MessageBox.Show($"Не выбраны коллизии для отображения номеров");
            return;
        }

        foreach (ClashEntity clash in DgClashes.SelectedItems)
        {
            var X = clash.X;
            var Y = clash.Y;
            var Z = clash.Z;
            PML.CreateCommand($"AID TEXT NUMBER 66 {clash.Id} AT E {X} N {Y} U {Z}").RunInPdms();
            PML.CreateCommand($"!!gph3ddesign1.view.THROUGH = ").RunInPdms();
        }

    }
    private void Add_Select1_MouseRightButtonDown(object sender, RoutedEventArgs e)
    {
        if (DgClashes.SelectedItem == null) return;
        ClashEntity selectedClash = (ClashEntity)DgClashes.SelectedItem;
        string Add1 = selectedClash.FirstElement;
        var dbElement = DbElement.GetElement(Add1);

        if (dbElement.IsNull)
        {
            System.Windows.MessageBox.Show($"Элемент {Add1} не найден");
            return;

        }
        PML.CreateCommand($"Add {Add1}").RunInPdms();
        PML.CreateCommand($"enhance {Add1} colour green").RunInPdms();
    }
    private void Add_Select2_MouseRightButtonDown(object sender, RoutedEventArgs e)
    {
        if (DgClashes.SelectedItem == null) return;
        ClashEntity selectedClash = (ClashEntity)DgClashes.SelectedItem;
        string Add2 = selectedClash.SecondElement;
        var dbElement = DbElement.GetElement(Add2);

        if (dbElement.IsNull)
        {
            System.Windows.MessageBox.Show($"Элемент {Add2} не найден");
            return;

        }
        PML.CreateCommand($"Add {Add2}").RunInPdms();
        PML.CreateCommand($"enhance {Add2} colour red").RunInPdms();
    }

    private void BtnShowElements_Click(object sender, RoutedEventArgs e)
    {
        var selectedClashes = DgClashes.SelectedItems
            .Cast<ClashEntity>()
            .ToList();

        if (selectedClashes.Count == 0)
        {
            MessageBox.Show("Выберите хотя бы одну коллизию.");
            return;
        }

        var elementRefs = selectedClashes
            .SelectMany(clash => new[] { clash.FirstElement, clash.SecondElement })
            .Where(elementRef => !string.IsNullOrWhiteSpace(elementRef))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        int addedCount = 0;
        foreach (string elementRef in elementRefs)
        {
            DbElement element = DbElement.GetElement(elementRef);
            if (element.IsNull || !element.IsValid)
                continue;

            PML.CreateCommand($"Add {elementRef}").RunInPdms();
            addedCount++;
        }

        MessageBox.Show($"В Drawlist добавлено элементов: {addedCount}.");
    }

    private void BtnCheck_Click(object sender, RoutedEventArgs e)
    {
        if (CurrZone == "ALL" || CurrZone == "CE")
        {
            MessageBox.Show("Для проверки выберите конкретную зону.");
            return;
        }

        logic.CheckZone(CurrZone, true);

        UpdateZoneInfo();
        Refresh();
    }
    private void BtnApprove_Click(object sender, RoutedEventArgs e)
    {
        if (DgClashes.SelectedItems.Count == 0)
        {
            MessageBox.Show("Выберите хотя бы одну коллизию", "Согласование", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        CbApproveReason.SelectedIndex= 0;
        ApproveOverlay.Visibility = Visibility.Visible;
        
    }
    private void BtnApproveCancel_Click(object sender, RoutedEventArgs e)
    {
        ApproveOverlay.Visibility = Visibility.Collapsed;
    }
    private void BtnApproveOk_Click(object sender, RoutedEventArgs e)
    {
        var selectedReasonItem = CbApproveReason.SelectedItem as ComboBoxItem;
        if (selectedReasonItem == null)

        {
            MessageBox.Show("Выберите причину согласования");
            return;
        }
        //получаю текст причины
        string reason = selectedReasonItem.Content?.ToString();
        if (string.IsNullOrWhiteSpace(reason))
        {
            MessageBox.Show("Причина согласования не определена");
            return;
        }
        //скрываем панель
        ApproveOverlay.Visibility = Visibility.Collapsed;
        ApproveSelectedClashes(reason);
    }
    private void ApproveSelectedClashes(string reason)
    {
        var Date = DateTime.Now;
        var Selected = DgClashes.SelectedItems.Cast<ClashEntity>().ToList();
        if (Selected.Count == 0)
        {
            System.Windows.MessageBox.Show("не выбраны коллизии для согласования");
            return;
        }
        foreach (var item in Selected)
        {
            var Id = item.Id;
            var Dept1 = item.FirstDept;
            var Dept2 = item.SecondDept;
            var RequestToDept = item.RequestToDept;
            var ApproveUser = item.ApproveUser;
            var ApproveDate = item.ApproveDate;
            var ApproveReason = item.ApproveReason;
            var InWorkUser = item.InWorkUser;
            var InWorkDate = item.InWorkDate;

            bool hasRequest = !string.IsNullOrWhiteSpace(item.RequestUser) || item.RequestDate != null || !string.IsNullOrWhiteSpace(RequestToDept);
            bool isInternalDepartmentClash = string.Equals(Dept1, Dept2, StringComparison.OrdinalIgnoreCase);
            bool isMyDept = hasRequest ? HasDepartmentAccess(RequestToDept) : isInternalDepartmentClash && HasDepartmentAccess(Dept1);
            bool hasApprove = !string.IsNullOrWhiteSpace(ApproveUser) || ApproveDate != null || !string.IsNullOrWhiteSpace(ApproveReason);
            bool hasInWork = !string.IsNullOrWhiteSpace(InWorkUser) || InWorkDate != null;

            if (!isMyDept)
            {
                System.Windows.MessageBox.Show("согласовать можно только колиизии по которым есть запрос в ваш отдел (колонка RequestTo) или коллизии внутри отдела");
                return;
            }

            if (hasApprove)
            {
                System.Windows.MessageBox.Show($"нельзя согласовать уже согласованную коллизию (Id={Id})");
                return;
            }
            if (hasInWork)
            {
                System.Windows.MessageBox.Show($"нельзя согласовать приятую в работу коллизию (Id={Id})");
                return;
            }
        }

        var Ids = new List<int>();
        var idsWithRequest = new List<int>();
        var idsWithNotRequest = new List<int>();
        foreach (var item in Selected)
        {
            var Id = item.Id;
            bool hasRequest = !string.IsNullOrWhiteSpace(item.RequestUser) || item.RequestDate != null || !string.IsNullOrWhiteSpace(item.RequestToDept);
            bool isMyDept = string.Equals(item.FirstDept, item.SecondDept,StringComparison.OrdinalIgnoreCase) && HasDepartmentAccess(item.FirstDept);


            if (hasRequest)
            {
                idsWithRequest.Add(item.Id);
            }
            else if (!hasRequest && isMyDept)
            {
                idsWithNotRequest.Add(item.Id);
            }
            else
            {
                System.Windows.MessageBox.Show("сбой при согласовании коллизий(Approve). обратитесь в ОАП");
                return;
            }
        }

        using (SqlConnection clashConnection = new SqlConnection(ClashConnectionString))
        {
            clashConnection.Open();

            try
            {
                if (idsWithRequest.Count > 0)
                {
                    clashConnection.Execute($@"UPDATE {ClashTableName}
                                            SET [AU] = @ApproveUser, 
                                                [AD] = @ApproveDate, 
                                                [AR] = @ApproveReason 
                                            WHERE id IN @ids",
                   new
                   {
                       ApproveUser = MyUlogId,
                       ApproveDate = Date,
                       ApproveReason = reason,
                       ids = idsWithRequest
                   });
                }

            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message + "\n" + ex.StackTrace);
            }

            try
            {
                if (idsWithNotRequest.Count > 0)
                {
                    clashConnection.Execute($@"UPDATE {ClashTableName}
                                            SET [RT] = @RequestTo, 
                                                [RU] = @RequestUser, 
                                                [RD] = @Date, 
                                                [AU] = @MyUlogId, 
                                                [AD] = @Date, 
                                                [AR] = @ApproveReason 
                                            WHERE id IN @ids",
                                           new
                                           {
                                               RequestTo = MyDept,
                                               RequestUser = MyUlogId,
                                               Date = Date,
                                               MyUlogId = MyUlogId,
                                               ApproveReason = reason,
                                               ids = idsWithNotRequest
                                           });
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message + "\n" + ex.StackTrace);
            }
        }


        Refresh();
        ShowZoneApprovalStatus();

    }
    private void BtnTakeInWork_Click(object sender, RoutedEventArgs e)
    {
        var Date = DateTime.Now;
        var Selected = DgClashes.SelectedItems.Cast<ClashEntity>().ToList();
        if (Selected.Count == 0)
        {
            System.Windows.MessageBox.Show("не выбраны коллизии для принятия в работу");
            return;
        }
        var ids = new List<int>();
        foreach (var item in Selected)
        {
            var Id = item.Id;
            var ApproveUser = item.ApproveUser;
            var ApproveDate = item.ApproveDate;
            var ApproveReason = item.ApproveReason;
            var InWorkUser = item.InWorkUser;
            var InWorkDate = item.InWorkDate;

            bool hasApprove = !string.IsNullOrWhiteSpace(ApproveUser) || ApproveDate != null || !string.IsNullOrWhiteSpace(ApproveReason);
            bool hasInWork = !string.IsNullOrWhiteSpace(InWorkUser) || InWorkDate != null;
            if (!HasDepartmentAccess(item.RequestToDept))
            {
                System.Windows.MessageBox.Show($"взять в работу можно только колиизии по которым есть запрос в ваш отдел (колонка RequestTo) (Id={Id})");
                return;
            }
            if (hasApprove)
            {
                System.Windows.MessageBox.Show($"нельзя брать в работу уже согласованную коллизию (Id={Id})");
                return;
            }
            if (hasInWork)
            {
                System.Windows.MessageBox.Show($"коллизия уже прията в работу (Id={Id})");
                return;
            }
            //if (!canTakeInWork)
            //{
            //    System.Windows.MessageBox.Show("сбой при согласовании коллизий(TakeInWork). обратитесь в ОАП");
            //    return;
            //}
            ids.Add(item.Id);
        }

        //var groups = Selected.GroupBy(x=>x.FirstDept)

        using (SqlConnection clashConnection = new SqlConnection(ClashConnectionString))
        {
            clashConnection.Open();

            clashConnection.Execute($@"UPDATE {ClashTableName}
                                            SET [WU] = @InWorkUser, 
                                                [WD] = @InWorkDate 
                                            WHERE id in @ids",
                                       new
                                       {
                                           InWorkUser = MyUlogId,
                                           InWorkDate = Date,
                                           ids = ids
                                       });
        }







        Refresh();

    }
    private void ShowZoneApprovalStatus()
    {
        if (CurrZone == "ALL" || CurrZone == "CE")
            return;

        using SqlConnection clashConnection = new(ClashConnectionString);
        clashConnection.Open();

        int inconsistentCount = clashConnection.ExecuteScalar<int>(
            @$"SELECT COUNT(*)
               FROM {ClashTableName}
               WHERE ([G1] = @zoneRef OR [G2] = @zoneRef)
                 AND ([D1] = @department OR [D2] = @department)
                 AND ([AR] IS NULL OR [AR] = '')",
            new
            {
                zoneRef = CurrZone,
                department = MyDept
            });

        PML.CreateCommand(
            $"$p Несогласованных коллизий этой зоны для моего отдела: {inconsistentCount}")
            .RunInPdms();

        if (inconsistentCount == 0)
        {
            MessageBox.Show(
                $"{MyUlogId}, поздравляю! Все коллизии зоны {CurrZone}, " +
                $"относящиеся к отделу {MyDept}, согласованы.");
        }
    }

    private void BtnRequest_Click(object sender, RoutedEventArgs e)
    {
        var Date = DateTime.Now;
        var Selected = DgClashes.SelectedItems.Cast<ClashEntity>().ToList();
        if (Selected.Count == 0)
        {
            System.Windows.MessageBox.Show("не выбраны коллизии для отправки запроса");
            return;
        }

        foreach (var item in Selected)
        {
            var Id = item.Id;
            var Dept1 = item.FirstDept;
            var Dept2 = item.SecondDept;
            var RequestToDept = item.RequestToDept;
            var RequestUser = item.RequestUser;
            var RequestDate = item.RequestDate;
            var ApproveUser = item.ApproveUser;
            var ApproveDate = item.ApproveDate;
            var ApproveReason = item.ApproveReason;
            var InWorkUser = item.InWorkUser;
            var InWorkDate = item.InWorkDate;


            bool isMyDept = HasDepartmentAccess(Dept1);
            bool hasRequest = !string.IsNullOrWhiteSpace(RequestUser) || RequestDate != null || !string.IsNullOrWhiteSpace(RequestToDept);
            bool hasApprove = !string.IsNullOrWhiteSpace(ApproveUser) || ApproveDate != null || !string.IsNullOrWhiteSpace(ApproveReason);
            bool hasInWork =  !string.IsNullOrWhiteSpace(InWorkUser)  || InWorkDate != null;

           

                if (!isMyDept)
                {
                    MessageBox.Show($"Нельзя отправить запрос по коллизии {Id}.\n" + $"В атрибуте :DEPTS отсутствует отдел D1: {Dept1}.");
                    return;
                }

                if (hasRequest)
                {
                    System.Windows.MessageBox.Show($"запрос уже отправлен (Id={Id})");
                    return;
                }
                if (hasApprove)
                {
                    System.Windows.MessageBox.Show($"нельзя отправить запрос по уже согласованной коллизии (Id={Id})");
                    return;
                }
                if (hasInWork)
                {
                    System.Windows.MessageBox.Show($"нельзя отправить запрос по коллизии (Id={Id}), так как она уже принята в работу");
                    return;
                }
          

           
        }
        var groups = Selected.GroupBy(x => x.SecondDept);
        try
        {
            using (SqlConnection clashConnection = new SqlConnection(ClashConnectionString))
            {
                clashConnection.Open();
                foreach (var group in groups)
                {
                    var ids = group.Select(x => x.Id).ToList();
                    var RequestTo = group.Key;
                    if (ids.Count == 0) continue;
                    clashConnection.Execute($@"UPDATE {ClashTableName}
                                            SET [RT] = @RequestTo, 
                                                [RU] = @RequestUser, 
                                                [RD] = @Date
                                            WHERE id IN @Ids",
                       new
                       {
                           RequestTo = RequestTo,
                           RequestUser = MyUlogId,
                           Date = Date,
                           Ids = ids
                       });
                }
            }

        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message + "\n" + ex.StackTrace);
            return;
        }

         SendMailByRequest2(Selected);

        Refresh();

    }

    private void BtnReject_Click(object sender, RoutedEventArgs e)
    {
        var selectedClashes = DgClashes.SelectedItems
            .Cast<ClashEntity>()
            .ToList();

        if (selectedClashes.Count == 0)
        {
            MessageBox.Show("Выберите хотя бы одну коллизию для отклонения.");
            return;
        }

        var rejectedRequests = new List<(ClashEntity Clash, string ReturnToDept, string PreviousRequestUser)>();

        foreach (ClashEntity clash in selectedClashes)
        {
            bool hasRequest = !string.IsNullOrWhiteSpace(clash.RequestToDept)
                && !string.IsNullOrWhiteSpace(clash.RequestUser)
                && clash.RequestDate.HasValue;
            bool hasApprove = !string.IsNullOrWhiteSpace(clash.ApproveUser)
                || clash.ApproveDate.HasValue
                || !string.IsNullOrWhiteSpace(clash.ApproveReason);
            bool hasInWork = !string.IsNullOrWhiteSpace(clash.InWorkUser)
                || clash.InWorkDate.HasValue;

            if (!hasRequest)
            {
                MessageBox.Show($"По коллизии {clash.Id} запрос ещё не отправлен.");
                return;
            }

            if (!string.Equals(clash.RequestToDept, MyDept, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    $"Отклонить коллизию {clash.Id} может только отдел-получатель {clash.RequestToDept}.");
                return;
            }

            if (hasApprove || hasInWork)
            {
                MessageBox.Show(
                    $"Коллизию {clash.Id} нельзя отклонить: она уже согласована или принята в работу.");
                return;
            }

            if (string.Equals(clash.FirstDept, clash.SecondDept, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    $"Коллизию {clash.Id} нельзя вернуть: оба элемента относятся к отделу {clash.FirstDept}.");
                return;
            }

            bool requestSentToFirstDept = string.Equals(
                clash.FirstDept,
                clash.RequestToDept,
                StringComparison.OrdinalIgnoreCase);
            bool requestSentToSecondDept = string.Equals(
                clash.SecondDept,
                clash.RequestToDept,
                StringComparison.OrdinalIgnoreCase);

            if (!requestSentToFirstDept && !requestSentToSecondDept)
            {
                MessageBox.Show(
                    $"У коллизии {clash.Id} RequestTo ({clash.RequestToDept}) " +
                    $"не совпадает с D1 ({clash.FirstDept}) или D2 ({clash.SecondDept}).");
                return;
            }

            string returnToDept = requestSentToFirstDept
                    ? clash.SecondDept
                    : clash.FirstDept;

            if (string.IsNullOrWhiteSpace(returnToDept))
            {
                MessageBox.Show($"Для коллизии {clash.Id} не удалось определить отдел возврата.");
                return;
            }

            rejectedRequests.Add((clash, returnToDept, clash.RequestUser));
        }

        DateTime requestDate = DateTime.Now;

        using (SqlConnection clashConnection = new SqlConnection(ClashConnectionString))
        {
            clashConnection.Open();
            using SqlTransaction transaction = clashConnection.BeginTransaction();

            try
            {
                foreach (var rejectedRequest in rejectedRequests)
                {
                    int updatedCount = clashConnection.Execute(
                        $@"UPDATE [{ClashTableName}]
                           SET [RT] = @RequestTo,
                               [RU] = @RequestUser,
                               [RD] = @RequestDate
                           WHERE [ID] = @Id;",
                        new
                        {
                            RequestTo = rejectedRequest.ReturnToDept,
                            RequestUser = MyUlogId,
                            RequestDate = requestDate,
                            Id = rejectedRequest.Clash.Id
                        },
                        transaction);

                    if (updatedCount != 1)
                    {
                        throw new InvalidOperationException(
                            $"Не удалось отклонить коллизию {rejectedRequest.Clash.Id}.");
                    }
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                MessageBox.Show($"Не удалось отклонить выбранные коллизии.\n{ex.Message}");
                return;
            }
        }

        string project = Project.CurrentProject.Name;
        foreach (var userGroup in rejectedRequests.GroupBy(request => request.PreviousRequestUser))
        {
            List<int> ids = userGroup
                .Select(request => request.Clash.Id)
                .ToList();
            string subject = $"Запрос по коллизиям отклонён, проект {project}, зона {CurrZone}";
            string body = $"Запрос по зоне {CurrZone} возвращён в ваш отдел. Коллизий: {ids.Count} шт.<BR>"
                + "Номера коллизий:<BR>"
                + string.Join("<BR>", ids);

            string userMail = GetUserMail(userGroup.Key);
            if (string.IsNullOrWhiteSpace(userMail))
            {
                Logger.WriteLine(
                    $"У пользователя {userGroup.Key} не заполнен атрибут :UserMail. "
                    + "Уведомление об отклонении не отправлено.");
                continue;
            }

            SendMail(userMail, subject, body);
        }

        Refresh();
        MessageBox.Show($"Отклонено коллизий: {rejectedRequests.Count}.");
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        var type = "";
        var InconsistentCount = 0;
        var Gpset = DbElement.GetElement(CurrZone);
        bool isForeignDept = clash.GetDepartment(Gpset, "GPSET") != MyDept && clash.GetDepartment(Gpset, "GPSET") != "SOT" && MyDept == "OGS";
        try
        {
            type = Gpset.GetString(DbAttributeInstance.TYPE);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Ошибка в InsertOneCLash {ex.Message}");
        }

        if (type != "GPSET")
        {
            System.Windows.MessageBox.Show($"{CurrZone} не является комплектом. сдавать можно только комлект)");
            return;
        }



        if (isForeignDept)
        {
            var result = MessageBox.Show("Вы не можете сдавать комплект другого отдела! (NO - продолжить)", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.No) return;
        }
        using (SqlConnection clashConnection = new SqlConnection(ClashConnectionString))
        {
            var todayMidNight = DateTime.Today;
            clashConnection.Open();
            InconsistentCount = clashConnection.ExecuteScalar<int>($@"select count (*) 
                                                       FROM {ClashTableName}
                                                       WHERE ([G1] = @gpset OR [G2] = @gpset)
                                                       AND [WU] IS NULL
                                                       AND ([AR] IS NULL OR [AR] = '')
                                                       AND [DT] < @today",
                                                   new { gpset = CurrZone, today = todayMidNight });
        }

        if (InconsistentCount == 0)
        {
            try
            {
                if (UpdateStatusKomplect())
                { logic.Report(CurrZone); }
                else { System.Windows.MessageBox.Show($"хотя в базе по данному комплекту несогласованных коллизий не обнаружено. комплект должен быть проверен непосредственно пред сдачей, т.к. могут появятся новые коллизии"); }
            }
            catch (Exception exs)
            {
                System.Windows.MessageBox.Show(exs.Message + "\n" + exs.StackTrace);
            }

        }
        else
        {
            System.Windows.MessageBox.Show($"несогласованных коллизий этого комплекта {Gpset} {InconsistentCount} шт");
        }

    }
    private void DgClashes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TxtSelectedCount.Text = GetSelectedClashesText(DgClashes.SelectedItems.Count);
        UpdateActionButtonsState();
    }

    private string GetSelectedClashesText(int count)
    {
        int lastTwoDigits = count % 100;
        int lastDigit = count % 10;

        if (lastTwoDigits >= 11 && lastTwoDigits <= 14)
            return $"Выбрано: {count} коллизий";

        switch (lastDigit)
        {
            case 1:
                return $"Выбрана: {count} коллизия";

            case 2:
            case 3:
            case 4:
                return $"Выбрано: {count} коллизии";

            default:
                return $"Выбрано: {count} коллизий";
        }
    }

    private void UpdateActionButtonsState()
    {
        bool hasSelection = DgClashes.SelectedItems.Count > 0;

        BtnShowElements.IsEnabled = hasSelection;
        BtnRequest.IsEnabled = hasSelection;
        BtnReject.IsEnabled = hasSelection;
        BtnTakeInWork.IsEnabled = hasSelection;
        BtnApprove.IsEnabled = hasSelection;
    }
    private void Refresh()
    {
        if (_isRefreshing)
            return;
        _isRefreshing = true;
        try
        {

            LoadZone();
            if (CbZone.SelectedItem == null) return;
            var clashes = logic.Show(ClashTableName, CurrZone);
            if (clashes == null) return;

            var stat = CalculateStatistic(clashes);
            DgClashes.ItemsSource = clashes;
            UpdateActionButtonsState();

            UpdateStatusKomplect();
            UpdateCard(TxtAllClash, PbAll, TxtPercentAll, stat.Total, stat.Total);
            UpdateCard(TxtNewClash, PbNew, TxtPercentNew, stat.New, stat.Total);
            UpdateCard(TxtSendClash, PbSend, TxtPercentSend, stat.Request, stat.Total);
            UpdateCard(TxtApproveClash, PbApprove, TxtPercentApprove, stat.Approve, stat.Total);
            UpdateCard(TxtInWorkClash, PbInWork, TxtPercentInWork, stat.InWork, stat.Total);
            UpdateCard(TxtAllertClash, PbAllert, TxtPercentAllert, stat.RequestOut, stat.Request);

            // var view = CollectionViewSource.GetDefaultView(DgClashes.ItemsSource);
            // view.Filter = ClashUsermod1Filter;
        }
        finally
        { _isRefreshing = false; }

    }
    
    private bool UpdateStatusKomplect()
    {
        if (logic.IsGreenZone(CurrZone, ClashTableName))
        {
            Indicator.Background = Brushes.LightGreen;
            Indicator.ToolTip = "Проверка актуальна";
            TxtCheckStatus.Text = "Проверка актуальна";

            return true;

        }
        else

        {
            Indicator.Background = Brushes.IndianRed;
            Indicator.ToolTip = "Требуется проверка";
            TxtCheckStatus.Text = "Требуется проверка";
            return false;
        }

    }
   
    public ClashStatistics CalculateStatistic (List<ClashEntity> clashes)
    {
        var stat = new ClashStatistics();
        if (clashes == null)
            return stat;
        stat.Total = clashes.Count;
        foreach (var c in clashes)
        {
            c.Status = GetClasStatus(c);
            c.StatusAge = GetStatusAge(c);
            switch (c.Status)
            {
                case "Новая":
                    stat.New++;
                    break;
                case "Просрочен запрос":
                    stat.RequestOut++;
                    break;
                case "Отправлено":
                    stat.Request++;
                    break;
                case "В работе":
                    stat.InWork++;
                    break;
                case "Согласовано":
                    stat.Approve++;
                    break;
                case "Просрочена работа":
                    stat.RequestOut++;
                    break;
            }

            
        }
        return stat;
    }

    private void UpdateCard(TextBlock textBlock, ProgressBar progressBar,TextBlock PersentProgres ,int value, int total)
    {
        textBlock.Text = value.ToString();
     
        double percent = 0;
        if (total >0)
            percent = value * 100.0 / total;

        progressBar.Value = percent;
        PersentProgres.Text = $"{percent:0}%";
       
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        UpdateZoneInfo();
        Refresh();
    }

    private void ToggleColumns_Click(object sender, RoutedEventArgs e)
    {
        Visibility visibility = ToggleColumns.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        DataGridColumn[] columns =
        [
            CT,
            E1,
            E2,
            DT,
            X0,
            Y0,
            Z0
        ];

        foreach (DataGridColumn column in columns)
            column.Visibility = visibility;
    }

    private string GetClasStatus(ClashEntity c)
    {

        bool dontDate = c.RequestDate == null && c.ApproveDate == null && c.InWorkDate == null;
        bool newDate = (DateTime.Now - c.Date.Value).TotalDays <= 3;

        if (dontDate && newDate)
        {
            return "Новая";
        }
        if(c.RequestDate != null && c.ApproveDate == null && c.InWorkDate == null)
        {
            if ((DateTime.Now - c.RequestDate.Value).TotalDays > 7 && c.ApproveDate == null && c.InWorkDate == null)
                return "Просрочен запрос";
            return "Отправлено";
        }
        if (c.InWorkDate != null && c.ApproveDate == null)
        {
            if ((DateTime.Now - c.InWorkDate.Value).TotalDays > 30)
                return "Просрочена работа";
            return "В работе";
        }
        if (c.ApproveDate != null)
            return "Согласовано";
        return "Бeз статуса";
    }

    private string GetStatusAge(ClashEntity clash)
    {
        if (clash.ApproveDate.HasValue)
            return "";

        DateTime? statusDate = clash.InWorkDate ?? clash.RequestDate ?? clash.Date;
        if (!statusDate.HasValue)
            return "";

        int days = Math.Max(0, (DateTime.Now.Date - statusDate.Value.Date).Days);
        return $"{days} дн.";
    }
}
