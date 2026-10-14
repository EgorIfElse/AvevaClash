using ClashForKPI;
using ClashForKPI.Models;
using Aveva.Core.Database;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using VIewModel;
using Brushes = System.Windows.Media.Brushes;
using ClashService = global::ClashForKPI.ClashService;
using PML = Aveva.Core.Utilities.CommandLine.Command;


namespace ViewForm;

/// <summary>
/// Логика взаимодействия для MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _isRefreshing;
    private readonly ClashLauncher launcher = new ClashLauncher();
    private readonly ClashService clash;
    public string CurrZone = "";
    public string ClashTableName = "";
    public string ProjectName = "";
    public string MyDept = "";
    public HashSet<string> MyDepartments { get; private set; } =
    new(StringComparer.OrdinalIgnoreCase);
    public string MyUlogId = "";
    public string ClashConnectionString = "";
    private string suggestedZone = "";
    private int suggestedClashId;
    private int guideStepIndex;
    private bool guideElementsShown;
    private const string DefaultLogDirectoryPath = "C:\\AVEVA\\ClasherLogs\\ClashLog.log";
    private readonly ClashLogger Logger = new ClashLogger(DefaultLogDirectoryPath);
    private readonly (string Title, string Description, string Target, string Mechanics)[] guideSteps =
    [
        ("Точка входа: ваш комплект", "Каждая строка — это пересечение двух элементов. Сначала выберите комплект, за который отвечает ваша команда: дальше вы увидите только коллизии с его участием.", "Выберите свой комплект в верхнем списке. Зелёным отмечены сданные комплекты.", "Clash Viewer читает из SQL актуальные коллизии, дату последней проверки и разработчика зоны. Модель AVEVA на этом шаге не изменяется."),
        ("Запустить проверку", "Теперь запускаем реальную проверку комплекта. Делайте это, когда модель сохранена и вы готовы обновить её картину коллизий.", "Нажмите «Проверить». После успешного завершения обновится дата проверки.", "AVEVA проверяет элементы выбранной зоны против подготовленного списка препятствий. Найденные пересечения сверяются с SQL: старые подтверждаются, новые добавляются, исчезнувшие обрабатываются по workflow."),
        ("Разобраться в 3D", "Таблица показывает факт коллизии, а 3D отвечает на главный вопрос: что именно пересеклось и где это происходит.", "Сначала выделите строку коллизии, затем нажмите иконку куба «Показать элементы».", "Для выбранных строк передаются RefNo, типы элементов, статус и координаты в PML-функцию !!ClashPoint. Она добавляет элементы в 3D и центрирует вид."),
        ("Передать ответственность", "Выберите коллизию и посмотрите доступные действия. Они зависят от статуса строки и прав вашего отдела — так нельзя случайно отправить запрос от чужого комплекта.", "Выберите строку коллизии: после этого станут понятны доступные действия в нижней панели.", "Перед отправкой программа определяет сторону открытого комплекта, отдел-получатель и ответственного. Сначала уходит уведомление; SQL-поля запроса записываются только при успешной отправке письма."),
        ("Проверить след", "История хранит коллизии, которые перестали существовать, но уже участвовали в работе или согласовании. Так решения не исчезают бесследно.", "Включите History, чтобы увидеть историю выбранного комплекта.", "History читает отдельную SQL-таблицу по текущему комплекту. «Доп. столбцы» показывает технические данные, а Representation rules вызывает PML-настройки 3D-отображения.")
    ];
    public MainWindow()
    {
        InitializeComponent();
        clash = launcher.Service;
        MyDept = launcher.MyDept;
        MyDepartments = launcher.MyDepartments;
        MyUlogId = launcher.MyUlogId;
        ProjectName = Project.CurrentProject.Name;
        ClashTableName = $"clashtable{ProjectName}_TEST";
        ClashConnectionString = launcher.ClashConnectionString;
        CurrZone = "";
        LoadZone();
    }

    private void MainWindow_Closed(object sender, EventArgs e)
    {
        if (ToggleClashRules.IsChecked == true)
        {
            try
            {
                PML.CreateCommand("!!ClashRules(F)").RunInPdms();
            }
        catch (Exception ex)
        {
            Logger.WriteException("Не удалось вернуть исходные настройки отображения", ex);
            }
        }

        try
        {
            PML.CreateCommand("aid clear text all").RunInPdms();
        }
        catch (Exception ex)
        {
            Logger.WriteException("Не удалось очистить подписи коллизий в 3D-виде", ex);
        }

        try
        {
            Logger.FinishLog();
        }
        catch (Exception ex)
        {
            Logger.WriteException("Не удалось сохранить лог формы", ex);
        }
    }

    private void ToggleClashRules_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = ToggleClashRules.IsChecked == true;

        try
        {
            PML.CreateCommand(enabled ? "!!ClashRules(T)" : "!!ClashRules(F)").RunInPdms();
            Logger.WriteLine($"Отображение изоляции и резервных объёмов: {(enabled ? "включено" : "выключено")}.");
        }
        catch (Exception ex)
        {
            ToggleClashRules.IsChecked = !enabled;
            Logger.WriteException("Не удалось изменить отображение изоляции и резервных объёмов", ex);
            MessageBox.Show("Не удалось изменить отображение изоляции и резервных объёмов. Проверьте, что PML-функция !!ClashRules доступна.");
        }
    }

    private void BtnGuide_Click(object sender, RoutedEventArgs e)
    {
        guideStepIndex = 0;
        guideElementsShown = false;
        UserGuideOverlay.Visibility = Visibility.Visible;
        UpdateGuide();
    }

    private void BtnGuideClose_Click(object sender, RoutedEventArgs e)
    {
        CloseGuide();
    }

    private void BtnGuidePrevious_Click(object sender, RoutedEventArgs e)
    {
        if (guideStepIndex == 0)
            return;

        guideStepIndex--;
        UpdateGuide();
    }

    private void BtnGuideNext_Click(object sender, RoutedEventArgs e)
    {
        if (!CanMoveToNextGuideStep())
            return;

        if (guideStepIndex == guideSteps.Length - 1)
        {
            CloseGuide();
            return;
        }

        guideStepIndex++;
        UpdateGuide();
    }

    private void CloseGuide()
    {
        ClearGuideFocus();
        UserGuideOverlay.Visibility = Visibility.Collapsed;
    }

    private void UpdateGuide()
    {
        (string title, string description, string target, string mechanics) = guideSteps[guideStepIndex];
        TxtGuideStep.Text = $"МИССИЯ {guideStepIndex + 1:00} · ОБУЧЕНИЕ";
        TxtGuideTitle.Text = title;
        TxtGuideDescription.Text = description;
        TxtGuideTarget.Text = target;
        TxtGuideMechanics.Text = mechanics;
        TxtGuideProgress.Text = $"{guideStepIndex + 1} из {guideSteps.Length}";
        BtnGuidePrevious.IsEnabled = guideStepIndex > 0;
        BtnGuideNext.Content = guideStepIndex == guideSteps.Length - 1 ? "Готово" : "Далее";
        UserGuidePanel.HorizontalAlignment = guideStepIndex is 1 or 3 or 4 ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        SetGuideStepDots();
        GuideWarningPanel.Visibility = Visibility.Collapsed;
        UpdateGuideActionState();
    }

    private bool CanMoveToNextGuideStep()
    {
        if (guideStepIndex == 2)
        {
            if (DgClashes.SelectedItems.Count == 0)
            {
                ShowGuideWarning("Сначала выберите хотя бы одну строку коллизии в таблице.");
                return false;
            }

            if (!guideElementsShown)
            {
                ShowGuideWarning("Строка выбрана. Теперь нажмите иконку куба «Показать элементы».");
                return false;
            }
        }

        if (guideStepIndex == 3 && DgClashes.SelectedItems.Count == 0)
        {
            ShowGuideWarning("Для работы с запросом сначала выберите хотя бы одну строку коллизии.");
            return false;
        }

        if (guideStepIndex == 4 && ToggleHistory.IsChecked != true)
        {
            ShowGuideWarning("Включите переключатель History, чтобы увидеть историю выбранного комплекта.");
            return false;
        }

        return true;
    }

    private void UpdateGuideActionState()
    {
        if (UserGuideOverlay?.Visibility != Visibility.Visible)
            return;

        if (guideStepIndex == 2)
        {
            TxtGuideTarget.Text = DgClashes.SelectedItems.Count == 0
                ? "Выберите одну или несколько строк коллизий в таблице."
                : guideElementsShown
                    ? "Готово: элементы переданы в 3D-вид AVEVA. Можно переходить далее."
                    : "Строка выбрана. Теперь нажмите иконку куба «Показать элементы».";
        }

        if (guideStepIndex == 3)
        {
            TxtGuideTarget.Text = DgClashes.SelectedItems.Count == 0
                ? "Выберите строку коллизии — после этого станут доступны допустимые действия."
                : "Строка выбрана. Доступные действия зависят от статуса коллизии и вашего отдела.";
        }

        if (guideStepIndex == 4 && ToggleHistory.IsChecked == true)
            TxtGuideTarget.Text = "History включён: отображаются ранее исчезнувшие коллизии выбранного комплекта.";

        ApplyGuideFocus();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(UpdateGuideSpotlight));
    }

    private FrameworkElement GetGuideFocusElement()
    {
        if (guideStepIndex == 0)
            return ZoneSelectorBorder;

        if (guideStepIndex == 1)
            return BtnCheck;

        if ((guideStepIndex is 2 or 3) && DgClashes.SelectedItems.Count == 0)
            return DgClashes;

        if (guideStepIndex == 2)
            return BtnShowElements;

        if (guideStepIndex == 3)
            return BtnRequest;

        return HistoryToggleBorder;
    }

    private void ApplyGuideFocus()
    {
        ClearGuideFocus();

        FrameworkElement focusElement = GetGuideFocusElement();

        if (focusElement is Border border)
            HighlightGuideBorder(border);
        else if (focusElement is Control control)
            HighlightGuideControl(control);
    }

    private void UserGuideOverlay_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (UserGuideOverlay.Visibility == Visibility.Visible)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(UpdateGuideSpotlight));
    }

    private void UpdateGuideSpotlight()
    {
        if (UserGuideOverlay.Visibility != Visibility.Visible)
            return;

        FrameworkElement target = GetGuideFocusElement();

        if (target.ActualWidth <= 0 || target.ActualHeight <= 0)
            return;

        Point targetPosition = target.TransformToAncestor(UserGuideOverlay).Transform(new Point(0, 0));
        const double padding = 8;
        double x = Math.Max(0, targetPosition.X - padding);
        double y = Math.Max(0, targetPosition.Y - padding);
        double width = Math.Min(UserGuideOverlay.ActualWidth - x, target.ActualWidth + padding * 2);
        double height = Math.Min(UserGuideOverlay.ActualHeight - y, target.ActualHeight + padding * 2);
        double overlayWidth = UserGuideOverlay.ActualWidth;
        double overlayHeight = UserGuideOverlay.ActualHeight;

        Canvas.SetLeft(GuideShadeTop, 0); Canvas.SetTop(GuideShadeTop, 0); GuideShadeTop.Width = overlayWidth; GuideShadeTop.Height = y;
        Canvas.SetLeft(GuideShadeBottom, 0); Canvas.SetTop(GuideShadeBottom, y + height); GuideShadeBottom.Width = overlayWidth; GuideShadeBottom.Height = Math.Max(0, overlayHeight - y - height);
        Canvas.SetLeft(GuideShadeLeft, 0); Canvas.SetTop(GuideShadeLeft, y); GuideShadeLeft.Width = x; GuideShadeLeft.Height = height;
        Canvas.SetLeft(GuideShadeRight, x + width); Canvas.SetTop(GuideShadeRight, y); GuideShadeRight.Width = Math.Max(0, overlayWidth - x - width); GuideShadeRight.Height = height;
        Canvas.SetLeft(GuideSpotlightFrame, x); Canvas.SetTop(GuideSpotlightFrame, y); GuideSpotlightFrame.Width = width; GuideSpotlightFrame.Height = height;
    }

    private void ShowGuideWarning(string message)
    {
        TxtGuideWarning.Text = message;
        GuideWarningPanel.Visibility = Visibility.Visible;
    }

    private void SetGuideStepDots()
    {
        Border[] guideDots = [GuideDot1, GuideDot2, GuideDot3, GuideDot4, GuideDot5];

        for (int i = 0; i < guideDots.Length; i++)
        {
            bool isCurrentStep = i == guideStepIndex;
            guideDots[i].Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(isCurrentStep ? (byte)59 : (byte)241, isCurrentStep ? (byte)130 : (byte)245, isCurrentStep ? (byte)246 : (byte)249));
            guideDots[i].BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(isCurrentStep ? (byte)59 : (byte)226, isCurrentStep ? (byte)130 : (byte)232, isCurrentStep ? (byte)246 : (byte)240));
            guideDots[i].BorderThickness = new Thickness(1);
            guideDots[i].Opacity = i <= guideStepIndex ? 1 : 0.55;
        }
    }

    private void HighlightGuideControl(Control control)
    {
        control.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246));
        control.BorderThickness = new Thickness(3);
    }

    private static void HighlightGuideBorder(Border border)
    {
        border.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246));
        border.BorderThickness = new Thickness(3);
    }

    private void ClearGuideFocus()
    {
        ZoneSelectorBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 213, 219));
        ZoneSelectorBorder.BorderThickness = new Thickness(1);
        HistoryToggleBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 232, 240));
        HistoryToggleBorder.BorderThickness = new Thickness(1);

        Control[] controls = [BtnCheck, BtnShowElements, BtnRequest, DgClashes];
        foreach (Control control in controls)
        {
            control.ClearValue(Control.BorderBrushProperty);
            control.ClearValue(Control.BorderThicknessProperty);
        }
    }

    private bool HasDepartmentAccess(string department)
    {
        return !string.IsNullOrWhiteSpace(department) && MyDepartments.Contains(department);
    }

    private void ShowAccessDeniedOverlay(string department, string oppositeZone, int clashId, bool canOpenOppositeZone)
    {
        suggestedZone = oppositeZone;
        suggestedClashId = clashId;
        TxtAccessDeniedZone.Text = CurrZone;
        TxtAccessDeniedDept.Text = department;
        TxtAccessDeniedUser.Text = MyUlogId;
        TxtSuggestedZone.Text = oppositeZone;
        SuggestedZonePanel.Visibility = canOpenOppositeZone ? Visibility.Visible : Visibility.Collapsed;
        TxtAccessDeniedHint.Visibility = canOpenOppositeZone ? Visibility.Collapsed : Visibility.Visible;
        AccessDeniedOverlay.Visibility = Visibility.Visible;
    }

    private void SuggestedZone_Click(object sender, RoutedEventArgs e)
    {
        AccessDeniedOverlay.Visibility = Visibility.Collapsed;
        CbZone.SelectedValue = suggestedZone;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(SelectSuggestedClash));
    }

    private void SelectSuggestedClash()
    {
        ClashEntity targetClash = DgClashes.ItemsSource.Cast<ClashEntity>().FirstOrDefault(clash => clash.Id == suggestedClashId);

        if (targetClash == null)
            return;

        DgClashes.SelectedItems.Clear();
        DgClashes.SelectedItem = targetClash;
        DgClashes.ScrollIntoView(targetClash);
        DgClashes.UpdateLayout();
        DgClashes.Focus();
    }

    private void BtnAccessDeniedClose_Click(object sender, RoutedEventArgs e)
    {
        AccessDeniedOverlay.Visibility = Visibility.Collapsed;
    }

    private bool IsCurrentZoneFirstSide(ClashEntity clash)
    {
        return string.Equals(CurrZone, clash.FirstZone, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsFirstSideRequestSender(ClashEntity clash)
    {
        bool sameDepartment = string.Equals(clash.FirstDept, clash.SecondDept, StringComparison.OrdinalIgnoreCase);

        if (!sameDepartment)
            return IsCurrentZoneFirstSide(clash);

        bool currentUserIsU1 = string.Equals(MyUlogId, clash.FirstUserMode, StringComparison.OrdinalIgnoreCase);
        bool currentUserIsU2 = string.Equals(MyUlogId, clash.SecondUserMode, StringComparison.OrdinalIgnoreCase);

        if (currentUserIsU1 && !currentUserIsU2)
            return true;

        if (currentUserIsU2 && !currentUserIsU1)
            return false;

        return IsCurrentZoneFirstSide(clash);
    }

    private string GetRequestRecipientUser(ClashEntity clash)
    {
        return IsFirstSideRequestSender(clash) ? clash.SecondUserMode : clash.FirstUserMode;
    }

    private string GetRequestRecipientDept(ClashEntity clash)
    {
        return IsFirstSideRequestSender(clash) ? clash.SecondDept : clash.FirstDept;
    }
    private List<ClashEntity> SendRequestNotifications(List<ClashEntity> clashes)
    {
        var groupsByUser = clashes.GroupBy(GetRequestRecipientUser);
        List<string> sentUsers = [];
        List<string> failedUsers = [];
        List<ClashEntity> notifiedClashes = [];
        string currentUserMail = GetUserMail(MyUlogId);

        foreach (var userGroup in groupsByUser)
        {
            string user = userGroup.Key;
            List<ClashEntity> userClashes = userGroup.ToList();
            string subject = $"Запрос на согласование коллизий по проекту {ProjectName}, зона {CurrZone}";
            string body = BuildRequestEmailBody(userClashes);
            string userMail = GetUserMail(user);

            if (string.IsNullOrWhiteSpace(userMail))
            {
                Logger.WriteLine($"У пользователя {user} не заполнен атрибут :UserMail. Письмо не отправлено.", LogType.Error);
                failedUsers.Add(user);
                continue;
            }

            if (SendMailFromAdmin(currentUserMail, userMail, subject, body))
            {
                sentUsers.Add(user);
                notifiedClashes.AddRange(userClashes);
            }
            else
                failedUsers.Add(user);
        }

        string message;
        if (groupsByUser.Count() == 0)
            message = "Нет запросов для отправки.";
        else if (failedUsers.Count == 0)
            message = "Уведомления отправлены: " + string.Join(", ", sentUsers);
        else if (sentUsers.Count == 0)
            message = "Не удалось отправить уведомления: " + string.Join(", ", failedUsers);
        else
            message = "Уведомления отправлены: " + string.Join(", ", sentUsers) + "\nНе отправлены: " + string.Join(", ", failedUsers);

        MessageBox.Show(message);
        return notifiedClashes;
    }

    private string BuildRequestEmailBody(List<ClashEntity> clashes)
    {
        var body = new StringBuilder();
        body.Append($"<p>Прошу устранить или согласовать коллизии по зоне <b>{WebUtility.HtmlEncode(CurrZone)}</b>.</p>");
        body.Append($"<p>Количество коллизий: <b>{clashes.Count}</b>.</p>");
        body.Append(BuildClashTable(clashes));
        return body.ToString();
    }

    private string BuildRejectedEmailBody(List<ClashEntity> clashes)
    {
        var body = new StringBuilder();
        body.Append($"<p>Запрос по зоне <b>{WebUtility.HtmlEncode(CurrZone)}</b> отклонён и возвращён в ваш отдел.</p>");
        body.Append($"<p>Количество коллизий: <b>{clashes.Count}</b>.</p>");
        body.Append(BuildClashTable(clashes));
        return body.ToString();
    }

    private static string BuildClashTable(List<ClashEntity> clashes)
    {
        const string cellStyle = "border:1px solid #cbd5e1;padding:6px 8px;text-align:left;white-space:nowrap;";
        var body = new StringBuilder();
        body.Append("<table style='border-collapse:collapse;font-family:Segoe UI,Arial,sans-serif;font-size:12px;color:#334155;'>");
        body.Append($"<tr style='background:#f1f5f9;'><th style='{cellStyle}'>ID</th><th style='{cellStyle}'>Тип</th><th style='{cellStyle}'>R1</th><th style='{cellStyle}'>U1</th><th style='{cellStyle}'>D1</th><th style='{cellStyle}'>G1</th><th style='{cellStyle}'>R2</th><th style='{cellStyle}'>U2</th><th style='{cellStyle}'>D2</th><th style='{cellStyle}'>G2</th></tr>");

        foreach (ClashEntity clash in clashes)
        {
            body.Append("<tr>");
            body.Append($"<td style='{cellStyle}'>{clash.Id}</td><td style='{cellStyle}'>{Encode(clash.ClashType)}</td><td style='{cellStyle}'>{Encode(clash.FirstElement)}</td><td style='{cellStyle}'>{Encode(clash.FirstUserMode)}</td><td style='{cellStyle}'>{Encode(clash.FirstDept)}</td><td style='{cellStyle}'>{Encode(clash.FirstZone)}</td>");
            body.Append($"<td style='{cellStyle}'>{Encode(clash.SecondElement)}</td><td style='{cellStyle}'>{Encode(clash.SecondUserMode)}</td><td style='{cellStyle}'>{Encode(clash.SecondDept)}</td><td style='{cellStyle}'>{Encode(clash.SecondZone)}</td>");
            body.Append("</tr>");
        }

        body.Append("</table>");
        return body.ToString();
    }

    private static string Encode(string value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }

    private string GetUserMail(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return string.Empty;

        try
        {
            DbElement ulog = DbElement.GetElement($"/{userName}");

            if (ulog.IsNull || !ulog.IsValid)
                return string.Empty;

            DbAttribute mailAttribute = DbAttribute.GetDbAttribute(":UserMail");
            string email = ulog.GetAsString(mailAttribute)?.Trim() ?? string.Empty;
            return email == "unset" ? string.Empty : email;
        }
        catch (Exception ex)
        {
            Logger.WriteException($"Не удалось получить :UserMail пользователя {userName}", ex);
            return string.Empty;
        }
    }

    private bool SendMailFromAdmin(string currentUserMail, string to, string subject, string body)
    {
        const string adminMail = "pdmsadmin@k-pei.ru";
        Logger.WriteLine($"Отправка письма пользователю {to} от служебного адреса {adminMail}. Копия: {currentUserMail}.");
        return SendMail(adminMail, to, currentUserMail, subject, body);
    }

    private bool SendMail(string from, string to, string copy, string subject, string body)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(from))
            {
                Logger.WriteLine("Не указан адрес отправителя. Письмо не отправлено.", LogType.Error);
                return false;
            }

            using var message = new MailMessage(from, to, subject, body)
            {
                IsBodyHtml = true,
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8
            };
            if (!string.IsNullOrWhiteSpace(copy))
                message.CC.Add(copy);

            using var smtp = new SmtpClient("mail.evgrp.ru", 25)
            {
                EnableSsl = false,
                UseDefaultCredentials = false
            };
            smtp.Send(message);
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteException($"Ошибка отправки письма от {from} пользователю {to}", ex);
            return false;
        }
    }

    private void LoadZone()
    {
        string currentSelected = CurrZone;
        var zoneItems = launcher.UpdateZoneList();
        CbZone.ItemsSource = zoneItems;

        if (!string.IsNullOrWhiteSpace(currentSelected))
            CbZone.SelectedValue = currentSelected;
    }

    private void CbZone_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CbZone.SelectedValue == null)
            return;

        CurrZone = CbZone.SelectedValue.ToString();
        Refresh();
    }

    private void UpdateZoneInfo()
    {
        TxtLastCheck.Text = "—";
        TxtDesigner.Text = "—";
        SetLastCheckColor(0x64, 0x74, 0x8B);

        if (string.IsNullOrWhiteSpace(CurrZone) || CurrZone == "ALL" || CurrZone == "CE")
            return;

        DbElement zone = DbElement.GetElement(CurrZone);
        if (zone.IsNull || !zone.IsValid)
            return;

        try
        {
            TxtDesigner.Text = zone.GetAsString(DbAttribute.GetDbAttribute(":Designer"));
        }
        catch (Exception ex)
        {
            Logger.WriteException($"Не удалось получить :Designer зоны {CurrZone}", ex);
        }

        try
        {
            DateTime lastCheck = launcher.GetZoneLastCheck(CurrZone);

            if (lastCheck != DateTime.MinValue)
                TxtLastCheck.Text = $"{lastCheck:dd.MM.yyyy HH:mm}";
        }
        catch (Exception ex)
        {
            Logger.WriteException($"Не удалось получить :Check зоны {CurrZone}", ex);
        }
    }

    private void SetLastCheckColor(byte red, byte green, byte blue)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        LastCheckIcon.Foreground = brush;
        TxtLastCheck.Foreground = brush;
    }
    private void BtnShowElements_Click(object sender, RoutedEventArgs e)
    {
        var selectedClashes = DgClashes.SelectedItems.Cast<ClashEntity>().ToList();

        Logger.WriteLine($"Запуск !!ClashPoint. Выбрано коллизий: {selectedClashes.Count}");

        foreach (ClashEntity clash in selectedClashes)
        {
            try
            {
                double status = GetPmlClashStatus(clash.Status);

                Logger.WriteLine(
                    $"Вызов !!ClashPoint: Status={status}; ID={clash.Id}; " +
                    $"One={clash.FirstElement}; Two={clash.SecondElement}; " +
                    $"E1={clash.FirstType}; E2={clash.SecondType}; " +
                    $"Pos=[{clash.X}, {clash.Y}, {clash.Z}]");

                PML.CreateCommand("!!ClashPointPos = Array()").RunInPdms();
                PML.CreateCommand($"!!ClashPointPos.Append({clash.X})").RunInPdms();
                PML.CreateCommand($"!!ClashPointPos.Append({clash.Y})").RunInPdms();
                PML.CreateCommand($"!!ClashPointPos.Append({clash.Z})").RunInPdms();

                PML.CreateCommand(
                    $"!!ClashPoint({status}, {clash.Id}, " +
                    $"'{clash.FirstElement}', " +
                    $"'{clash.SecondElement}', " +
                    $"'{clash.FirstType}', " +
                    $"'{clash.SecondType}', " +
                    $"!!ClashPointPos)")
                    .RunInPdms();

            }
            catch (Exception ex)
            {
                Logger.WriteException($"Ошибка вызова !!ClashPoint для коллизии {clash.Id}", ex);
            }
        }

        if (UserGuideOverlay?.Visibility == Visibility.Visible && guideStepIndex == 2 && selectedClashes.Count > 0)
        {
            guideElementsShown = true;
            UpdateGuideActionState();
        }

    }

    private double GetPmlClashStatus(string status)
    {
        switch (status)
        {
            case "Новая":
                return 1;

            case "Отправлено":
                return 2;

            case "В работе":
                return 3;

            case "Согласовано":
                return 4;

            case "Просрочен запрос":
                return 5;

            case "Просрочена работа":
                return 6;

            case "Без статуса":
            default:
                return 0;
        }
    }

    private void BtnCheck_Click(object sender, RoutedEventArgs e)
    {
        if (CurrZone == "ALL" || CurrZone == "CE")
        {
            MessageBox.Show("Для проверки выберите конкретную зону.");
            return;
        }

        DateTime previousCheck = launcher.GetZoneLastCheck(CurrZone);
        launcher.CheckZone(CurrZone, true);
        DateTime currentCheck = launcher.GetZoneLastCheck(CurrZone);
        bool checkCompleted = currentCheck > previousCheck;

        Refresh(checkCompleted);
    }
    private void BtnApprove_Click(object sender, RoutedEventArgs e)
    {
        CbApproveReason.SelectedIndex = 0;
        ApproveOverlay.Visibility = Visibility.Visible;
    }
    private void BtnApproveCancel_Click(object sender, RoutedEventArgs e)
    {
        ApproveOverlay.Visibility = Visibility.Collapsed;
    }
    private void BtnApproveOk_Click(object sender, RoutedEventArgs e)
    {
        var selectedReason = (ComboBoxItem)CbApproveReason.SelectedItem;
        string reason = selectedReason.Content.ToString();
        ApproveOverlay.Visibility = Visibility.Collapsed;
        ApproveSelectedClashes(reason);
    }
    private void ApproveSelectedClashes(string reason)
    {
        DateTime approveDate = DateTime.Now;
        List<ClashEntity> selectedClashes = DgClashes.SelectedItems.Cast<ClashEntity>().ToList();

        foreach (ClashEntity item in selectedClashes)
        {
            bool hasRequest = !string.IsNullOrWhiteSpace(item.RequestUser) || item.RequestDate != null || !string.IsNullOrWhiteSpace(item.RequestToDept);
            bool isInternalDepartmentClash = string.Equals(item.FirstDept, item.SecondDept, StringComparison.OrdinalIgnoreCase);
            bool isMyDept = hasRequest ? HasDepartmentAccess(item.RequestToDept) : isInternalDepartmentClash && HasDepartmentAccess(item.FirstDept);
            bool hasApprove = !string.IsNullOrWhiteSpace(item.ApproveUser) || item.ApproveDate != null || !string.IsNullOrWhiteSpace(item.ApproveReason);
            bool hasInWork = !string.IsNullOrWhiteSpace(item.InWorkUser) || item.InWorkDate != null;

            if (!isMyDept)
            {
                System.Windows.MessageBox.Show("согласовать можно только колиизии по которым есть запрос в ваш отдел (колонка RequestTo) или коллизии внутри отдела");
                return;
            }

            if (hasApprove)
            {
                System.Windows.MessageBox.Show($"нельзя согласовать уже согласованную коллизию (Id={item.Id})");
                return;
            }
            if (hasInWork)
            {
                System.Windows.MessageBox.Show($"нельзя согласовать приятую в работу коллизию (Id={item.Id})");
                return;
            }
        }

        var idsWithRequest = new List<int>();
        var clashesWithoutRequest = new List<ClashEntity>();
        foreach (ClashEntity item in selectedClashes)
        {
            bool hasRequest = !string.IsNullOrWhiteSpace(item.RequestUser) || item.RequestDate != null || !string.IsNullOrWhiteSpace(item.RequestToDept);
            if (hasRequest)
                idsWithRequest.Add(item.Id);
            else
                clashesWithoutRequest.Add(item);
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
                       ApproveDate = approveDate,
                       ApproveReason = reason,
                       ids = idsWithRequest
                   });
                }

            }
            catch (Exception ex)
            {
                Logger.WriteException("Ошибка записи согласования по полученным запросам", ex);
                System.Windows.MessageBox.Show(ex.Message + "\n" + ex.StackTrace);
            }

            try
            {
                foreach (var departmentGroup in clashesWithoutRequest.GroupBy(clash => clash.FirstDept))
                {
                    List<int> ids = departmentGroup.Select(clash => clash.Id).ToList();
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
                                               RequestTo = departmentGroup.Key,
                                               RequestUser = MyUlogId,
                                               Date = approveDate,
                                               MyUlogId = MyUlogId,
                                               ApproveReason = reason,
                                               ids
                                           });
                }
            }
            catch (Exception ex)
            {
                Logger.WriteException("Ошибка записи согласования без предварительного запроса", ex);
                System.Windows.MessageBox.Show(ex.Message + "\n" + ex.StackTrace);
            }
        }


        Refresh();

    }
    private void BtnTakeInWork_Click(object sender, RoutedEventArgs e)
    {
        DateTime inWorkDate = DateTime.Now;
        List<ClashEntity> selectedClashes = DgClashes.SelectedItems.Cast<ClashEntity>().ToList();
        var ids = new List<int>();

        foreach (ClashEntity item in selectedClashes)
        {
            bool hasApprove = !string.IsNullOrWhiteSpace(item.ApproveUser) || item.ApproveDate != null || !string.IsNullOrWhiteSpace(item.ApproveReason);
            bool hasInWork = !string.IsNullOrWhiteSpace(item.InWorkUser) || item.InWorkDate != null;
            if (!HasDepartmentAccess(item.RequestToDept))
            {
                System.Windows.MessageBox.Show($"взять в работу можно только колиизии по которым есть запрос в ваш отдел (колонка RequestTo) (Id={item.Id})");
                return;
            }
            if (hasApprove)
            {
                System.Windows.MessageBox.Show($"нельзя брать в работу уже согласованную коллизию (Id={item.Id})");
                return;
            }
            if (hasInWork)
            {
                System.Windows.MessageBox.Show($"коллизия уже принята в работу (Id={item.Id})");
                return;
            }
            ids.Add(item.Id);
        }

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
                                           InWorkDate = inWorkDate,
                                           ids = ids
                                       });
        }







        Refresh();

    }
    private void BtnRequest_Click(object sender, RoutedEventArgs e)
    {
        DateTime requestDate = DateTime.Now;
        List<ClashEntity> selectedClashes = DgClashes.SelectedItems.Cast<ClashEntity>().ToList();

        foreach (ClashEntity item in selectedClashes)
        {
            bool firstSideSendsRequest = IsFirstSideRequestSender(item);

            string senderDept;
            string recipientDept;
            string recipientUser;
            string oppositeZone;

            if (firstSideSendsRequest)
            {
                senderDept = item.FirstDept;
                recipientDept = item.SecondDept;
                recipientUser = item.SecondUserMode;
                oppositeZone = item.SecondZone;
            }
            else
            {
                senderDept = item.SecondDept;
                recipientDept = item.FirstDept;
                recipientUser = item.FirstUserMode;
                oppositeZone = item.FirstZone;
            }

            if (string.IsNullOrWhiteSpace(recipientDept))
            {
                string error = $"Коллизия {item.Id}: не заполнен отдел получателя. Отправка запроса отменена. Обратитесь к администратору AVEVA.";
                Logger.WriteLine(error, LogType.Error);
                MessageBox.Show(error);
                return;
            }

            if (string.IsNullOrWhiteSpace(recipientUser))
            {
                string error = $"Коллизия {item.Id}: не определён пользователь-получатель. Отправка запроса отменена. Обратитесь к администратору AVEVA.";
                Logger.WriteLine(error, LogType.Error);
                MessageBox.Show(error);
                return;
            }

            bool hasRequest = !string.IsNullOrWhiteSpace(item.RequestUser) || item.RequestDate != null || !string.IsNullOrWhiteSpace(item.RequestToDept);
            bool hasApprove = !string.IsNullOrWhiteSpace(item.ApproveUser) || item.ApproveDate != null || !string.IsNullOrWhiteSpace(item.ApproveReason);
            bool hasInWork = !string.IsNullOrWhiteSpace(item.InWorkUser) || item.InWorkDate != null;

            if (!HasDepartmentAccess(senderDept))
            {
                ShowAccessDeniedOverlay(senderDept, oppositeZone, item.Id, HasDepartmentAccess(recipientDept));
                return;
            }

            if (hasRequest)
            {
                System.Windows.MessageBox.Show($"запрос уже отправлен (Id={item.Id})");
                return;
            }
            if (hasApprove)
            {
                System.Windows.MessageBox.Show($"нельзя отправить запрос по уже согласованной коллизии (Id={item.Id})");
                return;
            }
            if (hasInWork)
            {
                System.Windows.MessageBox.Show($"нельзя отправить запрос по коллизии (Id={item.Id}), так как она уже принята в работу");
                return;
            }
        }
        List<ClashEntity> notifiedClashes = SendRequestNotifications(selectedClashes);

        if (notifiedClashes.Count == 0)
            return;

        var groups = notifiedClashes.GroupBy(GetRequestRecipientDept);
        try
        {
            using SqlConnection clashConnection = new(ClashConnectionString);
            clashConnection.Open();

            foreach (var group in groups)
            {
                List<int> ids = group.Select(clash => clash.Id).ToList();
                string requestToDept = group.Key;
                clashConnection.Execute($@"UPDATE [{ClashTableName}]
                                           SET [RT] = @RequestTo, [RU] = @RequestUser, [RD] = @RequestDate
                                           WHERE [ID] IN @Ids",
                    new { RequestTo = requestToDept, RequestUser = MyUlogId, RequestDate = requestDate, Ids = ids });
            }
        }
        catch (Exception ex)
        {
            Logger.WriteException("Ошибка записи отправленного запроса в SQL", ex);
            System.Windows.MessageBox.Show(ex.Message + "\n" + ex.StackTrace);
            return;
        }

        Refresh();
    }

    private void BtnReject_Click(object sender, RoutedEventArgs e)
    {
        List<ClashEntity> selectedClashes = DgClashes.SelectedItems.Cast<ClashEntity>().ToList();

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

            if (!HasDepartmentAccess(clash.RequestToDept))
            {
                MessageBox.Show($"Отклонить коллизию {clash.Id} может только пользователь отдела-получателя {clash.RequestToDept}.");
                return;
            }

            if (hasApprove || hasInWork)
            {
                MessageBox.Show(
                    $"Коллизию {clash.Id} нельзя отклонить: она уже согласована или принята в работу.");
                return;
            }

            if (clash.FirstDept == clash.SecondDept)
            {
                MessageBox.Show(
                    $"Коллизию {clash.Id} нельзя вернуть: оба элемента относятся к отделу {clash.FirstDept}.");
                return;
            }

            bool requestSentToFirstDept = clash.FirstDept == clash.RequestToDept;
            bool requestSentToSecondDept = clash.SecondDept == clash.RequestToDept;

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
                Logger.WriteException("Ошибка отклонения выбранных коллизий", ex);
                MessageBox.Show($"Не удалось отклонить выбранные коллизии.\n{ex.Message}");
                return;
            }
        }

        foreach (var userGroup in rejectedRequests.GroupBy(request => request.PreviousRequestUser))
        {
            List<ClashEntity> rejectedClashes = userGroup.Select(request => request.Clash).ToList();
            string subject = $"Запрос по коллизиям отклонён, проект {ProjectName}, зона {CurrZone}";
            string body = BuildRejectedEmailBody(rejectedClashes);

            string userMail = GetUserMail(userGroup.Key);
            if (string.IsNullOrWhiteSpace(userMail))
            {
                Logger.WriteLine(
                    $"У пользователя {userGroup.Key} не заполнен атрибут :UserMail. "
                    + "Уведомление об отклонении не отправлено.");
                continue;
            }

            SendMailFromAdmin(GetUserMail(MyUlogId), userMail, subject, body);
        }

        Refresh();
        MessageBox.Show($"Отклонено коллизий: {rejectedRequests.Count}.");
    }

    private void DgClashes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TxtSelectedCount.Text = GetSelectedClashesText(DgClashes.SelectedItems.Count);
        UpdateActionButtonsState();

        if (UserGuideOverlay?.Visibility == Visibility.Visible)
            UpdateGuideActionState();
    }

    private static string GetSelectedClashesText(int count)
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
        bool hasSelection = DgClashes.SelectedItems.Count > 0 && ToggleHistory.IsChecked != true;

        BtnShowElements.IsEnabled = hasSelection;
        BtnRequest.IsEnabled = hasSelection;
        BtnReject.IsEnabled = hasSelection;
        BtnTakeInWork.IsEnabled = hasSelection;
        BtnApprove.IsEnabled = hasSelection;
    }
    private void Refresh(bool zoneJustChecked = false, bool reloadZones = false)
    {
        if (_isRefreshing)
            return;

        _isRefreshing = true;
        try
        {
            if (reloadZones)
                LoadZone();

            if (CbZone.SelectedItem == null)
                return;

            UpdateZoneInfo();

            List<ClashEntity> clashes = ToggleHistory.IsChecked == true
                ? launcher.GetHistoryClashes(ClashTableName, CurrZone)
                : launcher.GetClashes(ClashTableName, CurrZone);
            if (clashes == null)
                return;

            ClashStatistics statistic = CalculateStatistic(clashes);
            DgClashes.ItemsSource = clashes;
            UpdateActionButtonsState();

            UpdateZoneCheckStatus(zoneJustChecked);
            UpdateCard(TxtAllClash, PbAll, TxtPercentAll, statistic.Total, statistic.Total);
            UpdateCard(TxtNewClash, PbNew, TxtPercentNew, statistic.New, statistic.Total);
            UpdateCard(TxtSendClash, PbSend, TxtPercentSend, statistic.Request, statistic.Total);
            UpdateCard(TxtApproveClash, PbApprove, TxtPercentApprove, statistic.Approve, statistic.Total);
            UpdateCard(TxtInWorkClash, PbInWork, TxtPercentInWork, statistic.InWork, statistic.Total);
            UpdateCard(TxtAllertClash, PbAllert, TxtPercentAllert, statistic.RequestOut, statistic.Total);
        }
        finally
        {
            _isRefreshing = false;
        }
    }
    
    private void UpdateZoneCheckStatus(bool zoneJustChecked)
    {
        bool isActual = zoneJustChecked || launcher.IsZoneCheckActual(CurrZone);

        if (isActual)
        {
            Indicator.Background = Brushes.LightGreen;
            Indicator.ToolTip = "Проверка актуальна";
            TxtCheckStatus.Text = "Проверка актуальна";
            SetLastCheckColor(0x3B, 0x82, 0xF6);
            return;
        }

        Indicator.Background = Brushes.IndianRed;
        Indicator.ToolTip = "Требуется проверка";
        TxtCheckStatus.Text = "Требуется проверка";

        if (TxtLastCheck.Text != "—")
            SetLastCheckColor(0xEF, 0x44, 0x44);
    }
   
    private ClashStatistics CalculateStatistic(List<ClashEntity> clashes)
    {
        var statistics = new ClashStatistics();
        statistics.Total = clashes.Count;

        foreach (ClashEntity clash in clashes)
        {
            clash.Status = GetClashStatus(clash);
            clash.StatusAge = GetStatusAge(clash);

            switch (clash.Status)
            {
                case "Новая":
                    statistics.New++;
                    break;
                case "Просрочен запрос":
                    statistics.RequestOut++;
                    break;
                case "Отправлено":
                    statistics.Request++;
                    break;
                case "В работе":
                    statistics.InWork++;
                    break;
                case "Согласовано":
                    statistics.Approve++;
                    break;
                case "Просрочена работа":
                    statistics.RequestOut++;
                    break;
            }
        }

        return statistics;
    }

    private static void UpdateCard(TextBlock valueText, ProgressBar progressBar, TextBlock percentText, int value, int total)
    {
        valueText.Text = value.ToString();

        double percent = 0;
        if (total > 0)
            percent = value * 100.0 / total;

        progressBar.Value = percent;
        percentText.Text = $"{percent:0.#}%";
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        Refresh(reloadZones: true);
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

    private void ToggleHistory_Click(object sender, RoutedEventArgs e)
    {
        Visibility historyVisibility = ToggleHistory.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HistoryDateColumn.Visibility = historyVisibility;
        HistoryReasonColumn.Visibility = historyVisibility;
        BtnCheck.IsEnabled = ToggleHistory.IsChecked != true;
        DgClashes.SelectedItems.Clear();
        Refresh();

        if (UserGuideOverlay?.Visibility == Visibility.Visible)
            UpdateGuideActionState();
    }

    private static string GetClashStatus(ClashEntity clash)
    {
        DateTime now = DateTime.Now;

        if (clash.ApproveDate.HasValue)
            return "Согласовано";

        if (clash.InWorkDate.HasValue)
        {
            if ((now - clash.InWorkDate.Value).TotalDays > 30)
                return "Просрочена работа";

            return "В работе";
        }

        if (clash.RequestDate.HasValue)
        {
            if ((now - clash.RequestDate.Value).TotalDays > 7)
                return "Просрочен запрос";

            return "Отправлено";
        }

        if ((now - clash.Date.Value).TotalDays <= 3)
            return "Новая";

        return "Без статуса";
    }

    private static string GetStatusAge(ClashEntity clash)
    {
        if (clash.ApproveDate.HasValue)
            return string.Empty;

        DateTime? statusDate = clash.InWorkDate ?? clash.RequestDate ?? clash.Date;
        if (!statusDate.HasValue)
            return string.Empty;

        int days = Math.Max(0, (DateTime.Now.Date - statusDate.Value.Date).Days);
        return $"{days} дн.";
    }
}
