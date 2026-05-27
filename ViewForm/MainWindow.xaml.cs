using Aveva.ClashChecker.NetCallable;
using Aveva.ClashChecker.NetCallable.Models;
using Aveva.Core.Database;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualBasic;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CC = global::ClashChecker.ClashChecker;
using CVF = global::ClashViewForm.ClashViewForm;
using PML = Aveva.Core.Utilities.CommandLine.Command;
using Brushes = System.Windows.Media.Brushes;
using System.Collections.Generic;

namespace ViewForm
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _isRefreshing;
        public CVF logic = new CVF();
        public CC clash = new CC();
        public string CurrGpset = "";
        public string ClashTableName = "";
        public string ProjectName = "";
        public string MyDept = "";
        public string MyUlogId = "";
        public string ClashConnectionString = "";
        private const string DefaultLogDirectoryPath = "D:\\AVEVA\\ClasherLogs\\ClashLog.log";
        private ClashLogger Logger { get; set; } = new ClashLogger(DefaultLogDirectoryPath);
        public MainWindow()
        {
            InitializeComponent();
            LoadGpset();

            TbMyDept.Text = logic.MyDept;
            TbMyUlogId.Text = logic.MyUlogId;
            MyDept = logic.MyDept;
            MyUlogId = logic.MyUlogId;
            ProjectName = Project.CurrentProject.Name;
            ClashTableName = $"clashtable{ProjectName}_TEST";
            ClashConnectionString = logic.ClashConnectionString;
            //string SelectedGpset = CbGpset.SelectedValue.ToString();
            CurrGpset = "";
        }





        private void LoadGpset()
        {
            string CurrentSelected = CurrGpset;
            var Proj = Project.CurrentProject.Name;
            ProjectName = Proj;
            var ClashConnectionString = logic.ClashConnectionString;
            using (SqlConnection clashConnection = new SqlConnection(ClashConnectionString))
            {
                clashConnection.Open();


                var GpsetItems = logic.UpdateGpsetList(ProjectName, clashConnection);

                CbGpset.DisplayMemberPath = "DisplayText";
                CbGpset.SelectedValuePath = "GpsetElement";
                CbGpset.ItemsSource = GpsetItems;

                if (!string.IsNullOrWhiteSpace(CurrentSelected))
                {
                    CbGpset.SelectedValue = CurrentSelected;
                }
            }

        }
        private void CbGpset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

            //System.Windows.MessageBox.Show($"gpset");
            if (CbGpset.SelectedValue == null)
                return;
            else
            {
                string SelectedGpset = CbGpset.SelectedValue.ToString();
                CurrGpset = SelectedGpset;
            }

            Refresh();
            // LoadClashEntity(SelectedGpset);
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
            ClashEntity selectedClash = (ClashEntity)DgClashes.SelectedItem;
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

            logic.CheckGpset(CurrGpset, 0, true, DefaultLogDirectoryPath);

            Refresh();
            LoadGpset();
        }
        private void BtnApprove_Click(object sender, RoutedEventArgs e)
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

                bool isMyDept = RequestToDept == MyDept || Dept1 == MyDept || Dept2 == MyDept;
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
            var Reason = Interaction.InputBox("Введите причину согласования", "Согласование", "Допустимая коллизия");
            if (Reason.Length < 5)
            {
                System.Windows.MessageBox.Show("Согласование отменено. Причина согласования не может быть менее 5 символов");
                return;
            }

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
                                            SET ApproveUser = @ApproveUser, 
                                                ApproveDate = @ApproveDate, 
                                                ApproveReason = @ApproveReason 
                                            WHERE id IN @ids",
                       new
                       {
                           ApproveUser = MyUlogId,
                           ApproveDate = Date,
                           ApproveReason = Reason,
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
                                            SET Requesttodept = @RequestTo, 
                                                Requestuser = @RequestUser, 
                                                Requestdate = @Date, 
                                                ApproveUser = @MyUlogId, 
                                                ApproveDate = @Date, 
                                                ApproveReason = @ApproveReason 
                                            WHERE id IN @ids",
                                               new
                                               {
                                                   RequestTo = MyDept,
                                                   RequestUser = MyUlogId,
                                                   Date = Date,
                                                   MyUlogId = MyUlogId,
                                                   ApproveReason = Reason,
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
                                                       WHERE (gpset1 = @gpset or gpset2 = @gpset)
                                                       AND (dept1 = @dept or dept2 = @dept)
                                                       AND (approveReason is null or approveReason = '')",
                                                       new 
                                                       { 
                                                        dept = MyDept,
                                                        gpset = CurrGpset
                                                       });
                PML.CreateCommand($"несогласованных коллизий этого комплекта моего отдела {InconsistentCount}").RunInPdms();
                if (InconsistentCount.Count == 0)
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
                                            SET InWorkUser = @InWorkUser, 
                                                InWorkDate = @InWorkDate 
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
                                                       WHERE (gpset1 = @gpset or gpset2 = @gpset)
                                                       AND (dept1 = @dept or dept2 = @dept)
                                                       AND (approveReason is null or approveReason = '')",
                                                       new 
                                                       { 
                                                        dept = MyDept,
                                                        gpset = CurrGpset
                                                       });
                PML.CreateCommand($"несогласованных коллизий этого комплекта моего отдела {InconsistentCount}").RunInPdms();
                if (InconsistentCount.Count == 0)
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
            var groups = Selected.GroupBy(x => x.FirstDept == MyDept ? x.SecondDept : x.FirstDept);
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
                                            SET requesttodept = @RequestTo, 
                                                requestuser = @RequestUser, 
                                                requestdate = @Date
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
                                                       WHERE (gpset1 = @gpset or gpset2 = @gpset)
                                                       AND (InWorkUser is null)
                                                       AND (approveReason is null or approveReason = '')
                                                       AND date < @today",
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
        private void TbMyDept_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
        private void TbMyUlogId_TextChanged(object sender, TextChangedEventArgs e)
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

                if (ChHideApproved.IsChecked == true)
                {
                    clashes = clashes.Where(x => string.IsNullOrEmpty(x.ApproveReason)).ToList();
                }
                if (ChHideOthersInWork.IsChecked == true)
                {
                    clashes = clashes.Where(x => string.IsNullOrEmpty(x.InWorkUser) || x.InWorkUser == MyUlogId).ToList();
                }
                if (ChOnlyRequestToMyDept.IsChecked == true)
                {
                    clashes = clashes.Where(x => x.RequestToDept == MyDept).ToList();
                }
                if (ChOnlyMyDept.IsChecked == true)
                {
                    clashes = clashes.Where(x => x.FirstDept == MyDept).ToList();
                }
                DgClashes.ItemsSource = clashes;
                TbStatus.Text = $"Всего коллизий {clashes.Count}";
            }
            finally
            { _isRefreshing = false; }





        }
        private void ChHideOthersInWork_Checked(object sender, RoutedEventArgs e)
        {
            Refresh();
        }
        private void ChOnlyRequestToMyDept_Checked(object sender, RoutedEventArgs e)
        {
            Refresh();
        }
        private void ChHideApproved_Checked(object sender, RoutedEventArgs e)
        {
            Refresh();
        }
        private void ChOnlyMyDept_Checked(object sender, RoutedEventArgs e)
        {
            Refresh();
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




    }

}
