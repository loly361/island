using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Markup;

namespace Island.Services;

/// <summary>
/// Interface language. Russian text is the key: <see cref="T(string)"/> returns the French translation when the
/// language is "fr" and falls back to the Russian original when a translation is missing.
/// The language is chosen once at startup from settings; a change applies after a restart.
/// </summary>
public static class Loc
{
    public const string Russian = "ru";
    public const string French = "fr";

    private static string _language = Russian;

    /// <summary>"ru" (default) or "fr"; anything else falls back to Russian.</summary>
    public static string Language
    {
        get => _language;
        set => _language = Normalize(value);
    }

    /// <summary>Culture for dates, weekdays and months shown to the user.</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(_language == French ? "fr-FR" : "ru-RU");

    public static string Normalize(string? language) =>
        string.Equals(language, French, StringComparison.OrdinalIgnoreCase) ? French : Russian;

    /// <summary>Translates a Russian interface string into the current language.</summary>
    public static string T(string ru) => Translate(ru, _language);

    /// <summary>Translates a format string, then fills it in with the current culture, exactly like an interpolated string.</summary>
    public static string F(string ru, params object?[] args) => string.Format(T(ru), args);

    public static string Translate(string ru, string? language) =>
        Normalize(language) == French && FrenchText.TryGetValue(ru, out var fr) ? fr : ru;

    public static bool HasTranslation(string ru) => FrenchText.ContainsKey(ru);

    /// <summary>Short day and month, e.g. 09.11 in Russian and 09/11 in French.</summary>
    public static string DayMonth(DateTime date) =>
        _language == French ? date.ToString("dd/MM", Culture) : date.ToString("dd.MM");

    private static readonly Dictionary<string, string> FrenchText = new(StringComparer.Ordinal)
    {
        // Tray menu
        ["Открыть"] = "Ouvrir",
        ["Запускать вместе с Windows"] = "Lancer au démarrage de Windows",
        ["Показывать уведомления Windows"] = "Afficher les notifications Windows",
        ["Папка с данными"] = "Dossier des données",
        ["Выход"] = "Quitter",

        // Tabs and header
        ["Главная"] = "Accueil",
        ["Буфер обмена"] = "Presse-papiers",
        ["Уведомления"] = "Notifications",
        ["Заметки, таймер и напоминания"] = "Notes, minuteur et rappels",
        ["Погода"] = "Météo",
        ["Управление"] = "Contrôles",
        ["Поиск"] = "Recherche",
        ["Настройки"] = "Paramètres",
        ["Закрепить открытым"] = "Garder ouvert",
        ["{0} — поиск"] = "{0} — recherche",

        // Home
        ["МУЗЫКА"] = "MUSIQUE",
        ["Ничего не играет"] = "Rien en lecture",
        ["Без названия"] = "Sans titre",
        ["Область"] = "Zone",
        ["Весь экран"] = "Plein écran",
        ["Текст с экрана"] = "Texte de l'écran",
        ["Пипетка"] = "Pipette",
        ["Чистый текст"] = "Texte brut",
        ["Скриншоты"] = "Captures",
        ["Не получилось"] = "Échec",
        ["В буфере нет текста"] = "Aucun texte dans le presse-papiers",
        ["Форматирование убрано"] = "Mise en forme supprimée",
        ["Текст в буфере теперь без оформления"] = "Le texte copié est désormais brut",
        ["Снимок экрана скопирован"] = "Capture d'écran copiée",
        ["Скриншот скопирован"] = "Capture copiée",
        ["Текст не найден"] = "Aucun texte trouvé",
        ["Выделите область покрупнее"] = "Sélectionnez une zone plus grande",
        ["Текст скопирован"] = "Texte copié",
        ["Цвет скопирован"] = "Couleur copiée",

        // Clipboard
        ["Поиск по буферу"] = "Rechercher dans le presse-papiers",
        ["Очистить (закреплённые остаются)"] = "Effacer (les éléments épinglés restent)",
        ["Здесь появится всё, что вы копируете"] = "Tout ce que vous copiez apparaîtra ici",
        ["Ничего не найдено"] = "Aucun résultat",
        ["Клик — вставить · Shift+клик — только копировать"] = "Clic — coller · Maj+clic — copier seulement",
        ["Скопировано"] = "Copié",
        ["Буфер"] = "Presse-papiers",
        ["Яндекс Браузер"] = "Yandex Browser",

        // Relative time
        ["сейчас"] = "maintenant",
        ["{0} мин"] = "{0} min",
        ["{0} ч"] = "{0} h",
        ["вчера"] = "hier",
        ["{0} дн"] = "{0} j",

        // Notifications
        ["Уведомления Windows"] = "Notifications Windows",
        ["Очистить список (в Windows уведомления остаются)"] = "Vider la liste (les notifications restent dans Windows)",
        ["Уведомление"] = "Notification",
        ["Показ уведомлений Windows выключен. Включить его можно в меню значка в трее."] =
            "L'affichage des notifications Windows est désactivé. Activez-le dans le menu de l'icône de la barre des tâches.",
        ["Windows не даёт доступ к уведомлениям, когда программа лежит в папке с очень длинным путём. Перенесите её, например, в Загрузки."] =
            "Windows refuse l'accès aux notifications quand le programme est dans un dossier au chemin très long. Déplacez-le, par exemple dans Téléchargements.",
        ["Нет доступа к уведомлениям Windows. Разрешите его в Параметры → Конфиденциальность и защита → Уведомления."] =
            "Pas d'accès aux notifications Windows. Autorisez-le dans Paramètres → Confidentialité et sécurité → Notifications.",
        ["Новых уведомлений нет"] = "Aucune nouvelle notification",

        // Notes
        ["Новая заметка"] = "Nouvelle note",
        ["Начните писать…"] = "Commencez à écrire…",
        ["Нажмите + и создайте заметку"] = "Appuyez sur + pour créer une note",
        ["Жирный"] = "Gras",
        ["Курсив"] = "Italique",
        ["Зачёркнутый"] = "Barré",
        ["Убрать форматирование"] = "Effacer la mise en forme",
        ["Маркированный список"] = "Liste à puces",
        ["Нумерованный список"] = "Liste numérotée",
        ["Задачи с галочками"] = "Liste de tâches",
        ["Напомнить об этой заметке"] = "Rappel pour cette note",
        ["Скопировать заметку"] = "Copier la note",
        ["Удалить заметку"] = "Supprimer la note",

        // Timer
        ["Старт"] = "Démarrer",
        ["Пауза"] = "Pause",
        ["Продолжить"] = "Reprendre",
        ["Сбросить"] = "Réinitialiser",
        ["Своё время"] = "Autre durée",
        ["мин"] = "min",
        ["Минут и Enter"] = "Minutes puis Entrée",
        ["Таймер завершён"] = "Minuteur terminé",
        ["{0} мин прошло"] = "{0} min écoulées",

        // Reminders (the parser understands French and Russian phrases)
        ["Напоминания"] = "Rappels",
        ["Напоминание"] = "Rappel",
        ["{0} и ещё {1}"] = "{0} et {1} de plus",
        ["Позвонить маме завтра в 9"] = "Appeler maman demain à 9",
        ["Напишите ниже, о чём и когда: «позвонить маме завтра в 9», «таблетки каждый день в 23:00», «через 20 минут выключить плиту»."] =
            "Écrivez ci-dessous quoi et quand, par exemple : «appeler maman demain à 9», «cachets tous les jours à 23:00», «dans 20 minutes éteindre la plaque».",
        ["Не удалось понять, когда напомнить. Например: «завтра в 9», «через 20 минут», «каждый день в 23:00»."] =
            "Impossible de comprendre quand rappeler. Par exemple : «demain à 9», «dans 20 minutes», «tous les jours à 23:00».",
        ["каждый день {0}"] = "chaque jour {0}",
        ["сегодня {0}"] = "aujourd'hui {0}",
        ["завтра {0}"] = "demain {0}",
        ["вс"] = "dim.",
        ["пн"] = "lun.",
        ["вт"] = "mar.",
        ["ср"] = "mer.",
        ["чт"] = "jeu.",
        ["пт"] = "ven.",
        ["сб"] = "sam.",

        // Weather
        ["Обновить"] = "Actualiser",
        ["ВЕТЕР"] = "VENT",
        ["ВЛАЖН."] = "HUMID.",
        ["ОСАДКИ"] = "PRÉCIP.",
        ["Данные Open-Meteo"] = "Données Open-Meteo",
        ["Нет данных"] = "Pas de données",
        ["Проверьте подключение к интернету и нажмите обновить."] = "Vérifiez la connexion Internet, puis actualisez.",
        ["Ощущается как {0}°"] = "Ressenti {0}°",
        ["Ощущается как {0}° · {1}°…{2}°"] = "Ressenti {0}° · {1}°…{2}°",
        ["{0:0.#} м/с"] = "{0:0.#} m/s",
        ["Направление: {0}"] = "Direction : {0}",
        ["{0:0.#} мм"] = "{0:0.#} mm",
        ["Восход {0:HH:mm} · закат {1:HH:mm}"] = "Lever {0:HH:mm} · coucher {1:HH:mm}",
        ["Сейчас"] = "Maint.",
        ["Сегодня"] = "Auj.",
        ["Вс"] = "Dim",
        ["Пн"] = "Lun",
        ["Вт"] = "Mar",
        ["Ср"] = "Mer",
        ["Чт"] = "Jeu",
        ["Пт"] = "Ven",
        ["Сб"] = "Sam",
        ["Ясно"] = "Dégagé",
        ["Преимущественно ясно"] = "Plutôt dégagé",
        ["Переменная облачность"] = "Partiellement nuageux",
        ["Пасмурно"] = "Couvert",
        ["Туман"] = "Brouillard",
        ["Морось"] = "Bruine",
        ["Ледяной дождь"] = "Pluie verglaçante",
        ["Дождь"] = "Pluie",
        ["Снег"] = "Neige",
        ["Ливень"] = "Averses",
        ["Снегопад"] = "Chutes de neige",
        ["Гроза"] = "Orage",
        ["С"] = "N",
        ["СВ"] = "NE",
        ["В"] = "E",
        ["ЮВ"] = "SE",
        ["Ю"] = "S",
        ["ЮЗ"] = "SO",
        ["З"] = "O",
        ["СЗ"] = "NO",

        // Control
        ["Включён"] = "Activé",
        ["Выключен"] = "Désactivé",
        ["Включена"] = "Activé",
        ["Выключена"] = "Désactivé",
        ["Подключён"] = "Connecté",
        ["Не настроен"] = "Non configuré",
        ["Недоступно"] = "Indisponible",
        ["Тёмная тема"] = "Thème sombre",
        ["Блокировка"] = "Verrouiller",
        ["Сон"] = "Veille",
        ["Перезагрузка"] = "Redémarrer",
        ["Выключение"] = "Éteindre",
        ["Точно?"] = "Sûr ?",

        // Search
        ["Программы, файлы, вопрос к ИИ"] = "Programmes, fichiers, question à l'IA",
        ["Программы, файлы из индекса Windows, вопрос к ИИ или поиск в браузере"] =
            "Programmes, fichiers de l'index Windows, question à l'IA ou recherche Web",
        ["↑↓ — выбор · Enter — открыть · Esc — закрыть"] = "↑↓ — choisir · Entrée — ouvrir · Échap — fermer",
        ["Приложение"] = "Application",
        ["Спросить ИИ: «{0}»"] = "Demander à l'IA : « {0} »",
        ["Искать в браузере: «{0}»"] = "Rechercher sur le Web : « {0} »",
        ["Поиск в интернете"] = "Recherche sur Internet",

        // App shortcuts (default names stored in settings.json)
        ["Проводник"] = "Explorateur",
        ["Терминал"] = "Terminal",
        ["Блокнот"] = "Bloc-notes",
        ["Калькулятор"] = "Calculatrice",
        ["Параметры"] = "Paramètres",

        // Settings
        ["ЯЗЫК"] = "LANGUE",
        ["Язык изменится после перезапуска."] = "La langue changera après le redémarrage.",
        ["РЕЖИМ"] = "MODE",
        ["Стандартный"] = "Standard",
        ["Минималистичный"] = "Minimaliste",
        ["Минималистичный: маленький кружок, при наведении открывается остров."] =
            "Minimaliste : un petit cercle qui ouvre l'île au survol.",
        ["ПОГОДА"] = "MÉTÉO",
        ["Город"] = "Ville",
        ["Например, Москва"] = "Par exemple, Paris",
        ["Широта"] = "Latitude",
        ["Долгота"] = "Longitude",
        ["ПОИСК И ИИ"] = "RECHERCHE ET IA",
        ["адрес с {0}"] = "adresse avec {0}",
        ["Спросить ИИ"] = "Demander à l'IA",
        ["ОФОРМЛЕНИЕ"] = "APPARENCE",
        ["Акцентный цвет"] = "Couleur d'accent",
        ["Из обоев"] = "Fond d'écran",
        ["Синий"] = "Bleu",
        ["Розовый"] = "Rose",
        ["Оранжевый"] = "Orange",
        ["Зелёный"] = "Vert",
        ["СИСТЕМА"] = "SYSTÈME",
        ["Порт уведомлений {0} нужен для хука Claude Code. Меняется в settings.json, после этого нужен перезапуск."] =
            "Le port de notifications {0} sert au hook Claude Code. Il se change dans settings.json, puis il faut redémarrer.",
        ["Файл настроек"] = "Fichier de paramètres",
        ["Ссылка должна начинаться с http или https и содержать {0} там, где вставляется запрос."] =
            "Le lien doit commencer par http ou https et contenir {0} à l'endroit où la requête est insérée.",
        ["Координата — число: широта от −90 до 90, долгота от −180 до 180."] =
            "Une coordonnée est un nombre : latitude de −90 à 90, longitude de −180 à 180.",
    };
}

/// <summary>
/// XAML access to <see cref="Loc.T(string)"/>: <c>Text="{loc:T Главная}"</c>, or with quotes when the text has commas or braces:
/// <c>{loc:T 'Например, Москва'}</c>. The value is resolved once when the element loads.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class T : MarkupExtension
{
    public T() { }

    public T(string text) => Text = text;

    [ConstructorArgument("text")]
    public string Text { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
