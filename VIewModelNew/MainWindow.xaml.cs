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
    public string CurrGpset = "";
    public string ClashTableName = "";
    public string ProjectName = "";
    public string MyDept = "";
    public string MyUlogId = "";
    public string ClashConnectionString = "";
    private const string DefaultLogDirectoryPath = "C:\\AVEVA\\ClasherLogs\\ClashLog.log";
    private ClashLogger Logger { get; set; } = new ClashLogger(DefaultLogDirectoryPath);
    public MainWindow()
    {
        InitializeComponent();
       
       
        LoadGpset();
      
        MyDept = logic.MyDept;
        MyUlogId = logic.MyUlogId;
        ProjectName = Project.CurrentProject.Name;
        ClashTableName = $"clashtable{ProjectName}_TEST";
        ClashConnectionString = logic.ClashConnectionString;
        //string SelectedGpset = CbGpset.SelectedValue.ToString();
        CurrGpset = "";
    }




    private void Notify_Onclick(object sender, RoutedEventArgs e)
    {
        
        
    }
    public void SendMailByRequest2(List<ClashEntity> rows)
    {
        
            string project = Project.CurrentProject.Name;
            var mailDict = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            if (MyDept != "SYSTEM")
            {
                if (r.FirstDept != MyDept && r.SecondDept != MyDept) continue;
                if (string.IsNullOrEmpty(r.RequestToDept)) continue;
                if (string.IsNullOrEmpty(r.RequestUser)) continue;
                if (!string.IsNullOrEmpty(r.ApproveUser)) continue;
                if (!string.IsNullOrEmpty(r.InWorkUser)) continue;
                if (r.RequestToDept == MyDept) continue;
            }

                string mailuser = r.RequestUser;

                if (!mailDict.ContainsKey(mailuser))
                    mailDict[mailuser] = new List<int>();
                mailDict[mailuser].Add(r.Id);
            }

            // отправка
            foreach (var kvp in mailDict)
            {
                string user = kvp.Key;
                List<int> ids = kvp.Value;
                string subject = $"Запрос на согласование коллизий по проекту {project} комплекту {CurrGpset}";
                string body = $"Прошу устранить или согласовать коллизии по комплекту {CurrGpset} в количестве {ids.Count} шт <BR>"
                               + "Номера коллизий: <BR>"
                               + string.Join("<BR>", ids);

                SendMail($"{user}@tep-m.ru", subject, body);

                var clashRow = rows.FirstOrDefault(r => r.RequestUser == user);
                if (clashRow != null)
                    SendCcByDept(clashRow.RequestToDept ?? "", subject, body, project);
            }

            string msg = mailDict.Count == 0
                ? "Нет запросов для отправки"
                : "Уведомления отправлены: " + string.Join(", ", mailDict.Keys);



            MessageBox.Show(msg);
        

    }

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
            SendMail($"{c}@tep-m.ru", subject, body);
    }

    private void SendMail(string to, string subject, string body)
    {
        try
        {
            string login = Project.CurrentProject.LoginUser.ToLower();
            var message = new MailMessage($"{login}@tep-m.ru", to, subject, body)
            {
                IsBodyHtml = true,
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8
            };
            var smtp = new SmtpClient("mail", 25)
            {
                EnableSsl = false,
                UseDefaultCredentials = false
            };
            smtp.Send(message);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Ошибка отправки на {to}: {ex.Message}");
        }
    }

    private void LoadGpset()
    {
        string CurrentSelected = CurrGpset;
       

            var GpsetItems = logic.UpdateGpsetList();

            CbGpset.DisplayMemberPath = "DisplayText";
            CbGpset.SelectedValuePath = "GpsetElement";
            CbGpset.ItemsSource = GpsetItems;
  
            if (!string.IsNullOrWhiteSpace(CurrentSelected))
            {
                CbGpset.SelectedValue = CurrentSelected;
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
    private void CbGpset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {

        if (CbGpset.SelectedValue == null)
            return;
        else
        {
            string SelectedGpset = CbGpset.SelectedValue.ToString();
            CurrGpset = SelectedGpset;
        }
       

            Refresh();
       
    }

    //  private void LoadClashEntity(string gpset)
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
    private void BtnCheck_Click(object sender, RoutedEventArgs e)
    {

        logic.CheckGpset(CurrGpset, 0, true);

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
            
            bool isMyDept = RequestToDept == MyDept || Dept1 == MyDept || Dept2 == MyDept || MyDept == "SYSTEM";
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

       // var Reason = Microsoft.VisualBasic.Interaction.InputBox("Введите причину согласования", "Согласование", "Допустимая коллизия");
       // if (Reason.Length < 5)
       // {
       //     System.Windows.MessageBox.Show("Согласование отменено. Причина согласования не может быть менее 5 символов");
       //     return;
       // }

        var Ids = new List<int>();
        var idsWithRequest = new List<int>();
        var idsWithNotRequest = new List<int>();
        foreach (var item in Selected)
        {
            var Id = item.Id;
            bool hasRequest = !string.IsNullOrWhiteSpace(item.RequestUser) || item.RequestDate != null || !string.IsNullOrWhiteSpace(item.RequestToDept);
            // bool hasNoRequest = !string.IsNullOrWhiteSpace(item.RequestUser)|| item.RequestDate == null || !string.IsNullOrWhiteSpace(item.RequestToDept);
            bool isMyDept = item.FirstDept == MyDept && item.SecondDept == MyDept;


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

        if (CurrGpset == "ALL" || CurrGpset == "CE") return;
        using (SqlConnection clashConnection = new SqlConnection(ClashConnectionString))
        {
            clashConnection.Open();
            var InconsistentCount = clashConnection.ExecuteScalar<int>($@"select count (*) 
                                                       FROM {ClashTableName}
                                                       WHERE ([G1] = @gpset OR [G2] = @gpset)
                                                       AND ([D1] = @dept OR [D2] = @dept)
                                                       AND ([AR] IS NULL OR [AR] = '')",
                                                   new
                                                   {
                                                       dept = MyDept,
                                                       gpset = CurrGpset
                                                   });
            PML.CreateCommand($"несогласованных коллизий этого комплекта моего отдела {InconsistentCount}").RunInPdms();
            if (InconsistentCount == 0)
            {
                System.Windows.MessageBox.Show($"{MyUlogId}, Поздравляю! согласованы все коллизии для комплекта {CurrGpset} относящиеся к отделу {MyDept}");
            }
        }



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
            //bool canTakeInWork = (item.RequestToDept == MyDept || MyDept == "SYSTEM") && !string.IsNullOrWhiteSpace(item.RequestUser) && item.RequestDate != null && (string.IsNullOrWhiteSpace(item.ApproveUser) && item.ApproveDate == null && string.IsNullOrWhiteSpace(item.ApproveReason));
            if (item.RequestToDept != MyDept && MyDept != "SYSTEM")
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

        if (CurrGpset == "ALL" || CurrGpset == "CE") return;
        using (SqlConnection clashConnection = new SqlConnection(ClashConnectionString))
        {
            clashConnection.Open();
            var InconsistentCount = clashConnection.ExecuteScalar<int>($@"select count (*) 
                                                       FROM {ClashTableName}
                                                       WHERE ([G1] = @gpset OR [G2] = @gpset)
                                                       AND ([D1] = @dept OR [D2] = @dept)
                                                       AND ([AR] IS NULL OR [AR] = '')",
                                                   new
                                                   {
                                                       dept = MyDept,
                                                       gpset = CurrGpset
                                                   });
            PML.CreateCommand($"несогласованных коллизий этого комплекта моего отдела {InconsistentCount}").RunInPdms();
            if (InconsistentCount == 0)
            {
                System.Windows.MessageBox.Show($"{MyUlogId}, Поздравляю! согласованы все коллизии для комплекта {CurrGpset} относящиеся к отделу {MyDept}");
            }
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


            bool isMyDept = Dept1 == MyDept || Dept2 == MyDept;
            bool hasRequest = !string.IsNullOrWhiteSpace(RequestUser) || RequestDate != null || !string.IsNullOrWhiteSpace(RequestToDept);
            bool hasApprove = !string.IsNullOrWhiteSpace(ApproveUser) || ApproveDate != null || !string.IsNullOrWhiteSpace(ApproveReason);

            if (MyDept != "SYSTEM")
            {


                if (!isMyDept)
                {
                    PML.CreateCommand($"{Id} - это коллизия других отделов ({Dept1} и {Dept2})").RunInPdms();
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
            }
            

            SendMailByRequest2(Selected);
        }
        var groups = Selected.GroupBy(x =>
           {
            if (x.FirstDept == MyDept || MyDept == "SYSTEM")
                return x.SecondDept;
             return x.FirstDept;
            });
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
        }




        Refresh();

    }
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        var type = "";
        var InconsistentCount = 0;
        var Gpset = DbElement.GetElement(CurrGpset);
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
            System.Windows.MessageBox.Show($"{CurrGpset} не является комплектом. сдавать можно только комлект)");
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
                                                   new { gpset = CurrGpset, today = todayMidNight });
        }

        if (InconsistentCount == 0)
        {
            try
            {
                if (UpdateStatusKomplect())
                { logic.Report(CurrGpset); }
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

    }
    private void Refresh()
    {
        if (_isRefreshing)
            return;
        _isRefreshing = true;
        try
        {

            LoadGpset();
            if (CbGpset.SelectedItem == null) return;
            var clashes = logic.Show(ClashTableName, CurrGpset);

            
            DgClashes.ItemsSource = clashes;
            
            UpdateStatusKomplect();
            clashes = DgClashes.ItemsSource as List<ClashEntity>;
            if (clashes == null) return;
            var stat = CalculateStatistic(clashes);
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
        if (logic.IsGreenGpset(CurrGpset, ClashTableName))
        {
            Indicator.Background = Brushes.LightGreen;
            Indicator.ToolTip = "Все коллизии согласованны";

            return true;

        }
        else

        {
            Indicator.Background = Brushes.IndianRed;
            Indicator.ToolTip = "Есть несогласованные коллизии";
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
        Refresh();
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
}
