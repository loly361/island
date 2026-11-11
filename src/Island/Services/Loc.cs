using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Markup;

namespace Island.Services;

/// <summary>
/// Interface language. Russian text is the key: <see cref="T(string)"/> returns the translation for the current
/// language ("fr" or "en") and falls back to the Russian original when a translation is missing.
/// The language is chosen once at startup from settings; a change applies after a restart.
/// </summary>
public static class Loc
{
    public const string Russian = "ru";
    public const string French = "fr";
    public const string English = "en";

    /// <summary>Every supported language, Russian first.</summary>
    public static IReadOnlyList<string> Languages { get; } = new[] { Russian, French, English };

    /// <summary>Languages that have a dictionary (all but Russian, which is the key itself).</summary>
    public static IReadOnlyList<string> TranslatedLanguages { get; } = new[] { French, English };

    private static string _language = Russian;

    /// <summary>"ru" (default), "fr" or "en"; anything else falls back to Russian.</summary>
    public static string Language
    {
        get => _language;
        set => _language = Normalize(value);
    }

    /// <summary>Culture for dates, weekdays and months shown to the user.</summary>
    public static CultureInfo Culture => CultureFor(_language);

    public static CultureInfo CultureFor(string? language) => CultureInfo.GetCultureInfo(Normalize(language) switch
    {
        French => "fr-FR",
        English => "en-US",
        _ => "ru-RU",
    });

    public static string Normalize(string? language)
    {
        if (string.Equals(language, French, StringComparison.OrdinalIgnoreCase)) return French;
        if (string.Equals(language, English, StringComparison.OrdinalIgnoreCase)) return English;
        return Russian;
    }

    /// <summary>Translates a Russian interface string into the current language.</summary>
    public static string T(string ru) => Translate(ru, _language);

    /// <summary>Translates a format string, then fills it in with the current culture, exactly like an interpolated string.</summary>
    public static string F(string ru, params object?[] args) => string.Format(T(ru), args);

    public static string Translate(string ru, string? language) =>
        Dictionary(language) is { } text && text.TryGetValue(ru, out var translated) ? translated : ru;

    /// <summary>True when every translated language (fr and en) has the key.</summary>
    public static bool HasTranslation(string ru) => TranslatedLanguages.All(l => HasTranslation(ru, l));

    /// <summary>True when the given language has the key; Russian always does, because the key is the Russian text.</summary>
    public static bool HasTranslation(string ru, string? language) =>
        Dictionary(language) is not { } text || text.ContainsKey(ru);

    /// <summary>All keys of a language's dictionary (empty for Russian).</summary>
    public static IReadOnlyCollection<string> Keys(string? language) =>
        Dictionary(language)?.Keys ?? (IReadOnlyCollection<string>)Array.Empty<string>();

    /// <summary>Short day and month: 09.11 in Russian, 09/11 in French, Nov 9 in English.</summary>
    public static string DayMonth(DateTime date) => DayMonth(date, _language);

    public static string DayMonth(DateTime date, string? language) => Normalize(language) switch
    {
        French => date.ToString("dd/MM", CultureFor(French)),
        English => date.ToString("MMM d", CultureFor(English)),
        _ => date.ToString("dd.MM", CultureInfo.InvariantCulture),
    };

    private static Dictionary<string, string>? Dictionary(string? language) => Normalize(language) switch
    {
        French => FrenchText,
        English => EnglishText,
        _ => null,
    };

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

        // Updates
        ["ОБНОВЛЕНИЯ"] = "MISES À JOUR",
        ["Версия {0}"] = "Version {0}",
        ["Проверять обновления автоматически"] = "Rechercher les mises à jour automatiquement",
        ["Вкл"] = "Activé",
        ["Выкл"] = "Désactivé",
        ["Проверить сейчас"] = "Vérifier maintenant",
        ["Обновить до {0}"] = "Mettre à jour vers {0}",
        ["Проверка…"] = "Vérification…",
        ["У вас последняя версия"] = "Vous avez la dernière version",
        ["Доступна версия {0}"] = "Version {0} disponible",
        ["Загрузка…"] = "Téléchargement…",
        ["Загрузка… {0}%"] = "Téléchargement… {0} %",
        ["Не удалось проверить обновления"] = "Impossible de vérifier les mises à jour",
        ["Не удалось скачать обновление"] = "Impossible de télécharger la mise à jour",
        ["Файл обновления повреждён или подменён"] = "Le fichier de mise à jour est endommagé ou modifié",
        ["Установка обновления…"] = "Installation de la mise à jour…",
        ["Не удалось запустить установщик"] = "Impossible de lancer le programme d'installation",
        ["Доступна новая версия Island"] = "Une nouvelle version d'Island est disponible",
        ["Версия {0}. Обновить можно в настройках."] = "Version {0}. Mise à jour depuis les paramètres.",

        // Screen recording and game mode
        ["Запись экрана"] = "Enregistrer l'écran",
        ["Видео"] = "Vidéos",
        ["Что записать?"] = "Que filmer ?",
        ["Без звука, до 30 минут"] = "Sans son, jusqu'à 30 minutes",
        ["Отмена"] = "Annuler",
        ["Остановить"] = "Arrêter",
        ["Остановить запись"] = "Arrêter l'enregistrement",
        ["Не удалось начать запись"] = "Impossible de démarrer l'enregistrement",
        ["Не удалось сохранить запись"] = "Impossible d'enregistrer la vidéo",
        ["Запись сохранена"] = "Enregistrement sauvegardé",
        ["{0} — нажмите, чтобы показать в папке"] = "{0} — cliquez pour l'afficher dans le dossier",
        ["Не хватает места на диске"] = "Espace disque insuffisant",
        ["Нет доступа к папке Видео"] = "Pas d'accès au dossier Vidéos",
        ["Достигнут предел {0} мин"] = "Limite de {0} min atteinte",
        ["Изменился экран"] = "L'écran a changé",
        ["Не удалось снять экран"] = "Impossible de capturer l'écran",
        ["Запись слишком короткая"] = "Enregistrement trop court",
        ["РЕЖИМ ДЛЯ ИГР"] = "MODE JEU",
        ["Прятать остров в играх и полноэкранных окнах"] = "Masquer l'île dans les jeux et les fenêtres plein écran",
        ["Уведомления, напоминания и таймер покажутся, когда полноэкранное окно закроется или свернётся."] = "Les notifications, rappels et minuteur s'afficheront quand la fenêtre plein écran sera fermée ou réduite.",
        ["без баннеров"] = "sans bannières",
        ["Баннеры от этой программы: выключить или включить"] = "Bannières de cette application : désactiver ou activer",
        ["УВЕДОМЛЕНИЯ"] = "NOTIFICATIONS",
        ["Не беспокоить"] = "Ne pas déranger",
        ["Баннеры уведомлений Windows и других программ не показываются. Таймер и напоминания работают как обычно."] = "Les bannières des notifications Windows et des autres programmes ne s'affichent pas. Le minuteur et les rappels fonctionnent comme d'habitude.",
        ["Программы без баннеров не выбраны. Выключить баннеры программы можно на вкладке уведомлений."] = "Aucune application sans bannières. Vous pouvez désactiver les bannières d'une application dans l'onglet des notifications.",
        ["Без баннеров: {0}"] = "Sans bannières : {0}",
        ["Перезапустить сейчас"] = "Redémarrer maintenant",
    };

    private static readonly Dictionary<string, string> EnglishText = new(StringComparer.Ordinal)
    {
        // Tray menu
        ["Открыть"] = "Open",
        ["Запускать вместе с Windows"] = "Start with Windows",
        ["Показывать уведомления Windows"] = "Show Windows notifications",
        ["Папка с данными"] = "Data folder",
        ["Выход"] = "Exit",

        // Tabs and header
        ["Главная"] = "Home",
        ["Буфер обмена"] = "Clipboard",
        ["Уведомления"] = "Notifications",
        ["Заметки, таймер и напоминания"] = "Notes, timer and reminders",
        ["Погода"] = "Weather",
        ["Управление"] = "Controls",
        ["Поиск"] = "Search",
        ["Настройки"] = "Settings",
        ["Закрепить открытым"] = "Keep open",
        ["{0} — поиск"] = "{0} — search",

        // Home
        ["МУЗЫКА"] = "MUSIC",
        ["Ничего не играет"] = "Nothing playing",
        ["Без названия"] = "Untitled",
        ["Область"] = "Region",
        ["Весь экран"] = "Full screen",
        ["Текст с экрана"] = "Screen text",
        ["Пипетка"] = "Color picker",
        ["Чистый текст"] = "Plain text",
        ["Скриншоты"] = "Screenshots",
        ["Не получилось"] = "Failed",
        ["В буфере нет текста"] = "No text in the clipboard",
        ["Форматирование убрано"] = "Formatting removed",
        ["Текст в буфере теперь без оформления"] = "Clipboard text is now plain",
        ["Снимок экрана скопирован"] = "Screenshot copied",
        ["Скриншот скопирован"] = "Screenshot copied",
        ["Текст не найден"] = "No text found",
        ["Выделите область покрупнее"] = "Select a larger area",
        ["Текст скопирован"] = "Text copied",
        ["Цвет скопирован"] = "Color copied",

        // Clipboard
        ["Поиск по буферу"] = "Search clipboard",
        ["Очистить (закреплённые остаются)"] = "Clear (pinned items stay)",
        ["Здесь появится всё, что вы копируете"] = "Everything you copy will appear here",
        ["Ничего не найдено"] = "Nothing found",
        ["Клик — вставить · Shift+клик — только копировать"] = "Click — paste · Shift+click — copy only",
        ["Скопировано"] = "Copied",
        ["Буфер"] = "Clipboard",
        ["Яндекс Браузер"] = "Yandex Browser",

        // Relative time
        ["сейчас"] = "now",
        ["{0} мин"] = "{0} min",
        ["{0} ч"] = "{0} h",
        ["вчера"] = "yesterday",
        ["{0} дн"] = "{0} d",

        // Notifications
        ["Уведомления Windows"] = "Windows notifications",
        ["Очистить список (в Windows уведомления остаются)"] = "Clear list (notifications stay in Windows)",
        ["Уведомление"] = "Notification",
        ["Показ уведомлений Windows выключен. Включить его можно в меню значка в трее."] =
            "Windows notifications are hidden. Turn them on in the tray icon menu.",
        ["Windows не даёт доступ к уведомлениям, когда программа лежит в папке с очень длинным путём. Перенесите её, например, в Загрузки."] =
            "Windows blocks notification access when the app sits in a folder with a very long path. Move it, for example, to Downloads.",
        ["Нет доступа к уведомлениям Windows. Разрешите его в Параметры → Конфиденциальность и защита → Уведомления."] =
            "No access to Windows notifications. Allow it in Settings → Privacy & security → Notifications.",
        ["Новых уведомлений нет"] = "No new notifications",

        // Notes
        ["Новая заметка"] = "New note",
        ["Начните писать…"] = "Start typing…",
        ["Нажмите + и создайте заметку"] = "Press + to create a note",
        ["Жирный"] = "Bold",
        ["Курсив"] = "Italic",
        ["Зачёркнутый"] = "Strikethrough",
        ["Убрать форматирование"] = "Clear formatting",
        ["Маркированный список"] = "Bulleted list",
        ["Нумерованный список"] = "Numbered list",
        ["Задачи с галочками"] = "Checklist",
        ["Напомнить об этой заметке"] = "Remind me about this note",
        ["Скопировать заметку"] = "Copy note",
        ["Удалить заметку"] = "Delete note",

        // Timer
        ["Старт"] = "Start",
        ["Пауза"] = "Pause",
        ["Продолжить"] = "Resume",
        ["Сбросить"] = "Reset",
        ["Своё время"] = "Custom",
        ["мин"] = "min",
        ["Минут и Enter"] = "Minutes, then Enter",
        ["Таймер завершён"] = "Timer done",
        ["{0} мин прошло"] = "{0} min elapsed",

        // Reminders (the parser understands English, French and Russian phrases)
        ["Напоминания"] = "Reminders",
        ["Напоминание"] = "Reminder",
        ["{0} и ещё {1}"] = "{0} and {1} more",
        ["Позвонить маме завтра в 9"] = "Call mom tomorrow at 9",
        ["Напишите ниже, о чём и когда: «позвонить маме завтра в 9», «таблетки каждый день в 23:00», «через 20 минут выключить плиту»."] =
            "Type what and when below: “call mom tomorrow at 9”, “pills every day at 11pm”, “in 20 minutes turn off the stove”.",
        ["Не удалось понять, когда напомнить. Например: «завтра в 9», «через 20 минут», «каждый день в 23:00»."] =
            "Couldn't tell when to remind you. Try: “tomorrow at 9”, “in 20 minutes”, “every day at 11pm”.",
        ["каждый день {0}"] = "daily {0}",
        ["сегодня {0}"] = "today {0}",
        ["завтра {0}"] = "tomorrow {0}",
        ["вс"] = "Sun",
        ["пн"] = "Mon",
        ["вт"] = "Tue",
        ["ср"] = "Wed",
        ["чт"] = "Thu",
        ["пт"] = "Fri",
        ["сб"] = "Sat",

        // Weather
        ["Обновить"] = "Refresh",
        ["ВЕТЕР"] = "WIND",
        ["ВЛАЖН."] = "HUMID.",
        ["ОСАДКИ"] = "PRECIP.",
        ["Данные Open-Meteo"] = "Data: Open-Meteo",
        ["Нет данных"] = "No data",
        ["Проверьте подключение к интернету и нажмите обновить."] = "Check your internet connection, then refresh.",
        ["Ощущается как {0}°"] = "Feels like {0}°",
        ["Ощущается как {0}° · {1}°…{2}°"] = "Feels like {0}° · {1}°…{2}°",
        ["{0:0.#} м/с"] = "{0:0.#} m/s",
        ["Направление: {0}"] = "Direction: {0}",
        ["{0:0.#} мм"] = "{0:0.#} mm",
        ["Восход {0:HH:mm} · закат {1:HH:mm}"] = "Sunrise {0:HH:mm} · sunset {1:HH:mm}",
        ["Сейчас"] = "Now",
        ["Сегодня"] = "Today",
        ["Вс"] = "Sun",
        ["Пн"] = "Mon",
        ["Вт"] = "Tue",
        ["Ср"] = "Wed",
        ["Чт"] = "Thu",
        ["Пт"] = "Fri",
        ["Сб"] = "Sat",
        ["Ясно"] = "Clear",
        ["Преимущественно ясно"] = "Mostly clear",
        ["Переменная облачность"] = "Partly cloudy",
        ["Пасмурно"] = "Overcast",
        ["Туман"] = "Fog",
        ["Морось"] = "Drizzle",
        ["Ледяной дождь"] = "Freezing rain",
        ["Дождь"] = "Rain",
        ["Снег"] = "Snow",
        ["Ливень"] = "Showers",
        ["Снегопад"] = "Snowfall",
        ["Гроза"] = "Thunderstorm",
        ["С"] = "N",
        ["СВ"] = "NE",
        ["В"] = "E",
        ["ЮВ"] = "SE",
        ["Ю"] = "S",
        ["ЮЗ"] = "SW",
        ["З"] = "W",
        ["СЗ"] = "NW",

        // Control
        ["Включён"] = "On",
        ["Выключен"] = "Off",
        ["Включена"] = "On",
        ["Выключена"] = "Off",
        ["Подключён"] = "Connected",
        ["Не настроен"] = "Not set up",
        ["Недоступно"] = "Unavailable",
        ["Тёмная тема"] = "Dark theme",
        ["Блокировка"] = "Lock",
        ["Сон"] = "Sleep",
        ["Перезагрузка"] = "Restart",
        ["Выключение"] = "Shut down",
        ["Точно?"] = "Sure?",

        // Search
        ["Программы, файлы, вопрос к ИИ"] = "Apps, files, ask AI",
        ["Программы, файлы из индекса Windows, вопрос к ИИ или поиск в браузере"] =
            "Apps, files from the Windows index, a question for AI or a web search",
        ["↑↓ — выбор · Enter — открыть · Esc — закрыть"] = "↑↓ — select · Enter — open · Esc — close",
        ["Приложение"] = "App",
        ["Спросить ИИ: «{0}»"] = "Ask AI: “{0}”",
        ["Искать в браузере: «{0}»"] = "Search the web: “{0}”",
        ["Поиск в интернете"] = "Web search",

        // App shortcuts (default names stored in settings.json)
        ["Проводник"] = "File Explorer",
        ["Терминал"] = "Terminal",
        ["Блокнот"] = "Notepad",
        ["Калькулятор"] = "Calculator",
        ["Параметры"] = "Settings",

        // Settings
        ["ЯЗЫК"] = "LANGUAGE",
        ["Язык изменится после перезапуска."] = "The language will change after a restart.",
        ["РЕЖИМ"] = "MODE",
        ["Стандартный"] = "Standard",
        ["Минималистичный"] = "Minimal",
        ["Минималистичный: маленький кружок, при наведении открывается остров."] =
            "Minimal: a small dot that opens the island on hover.",
        ["ПОГОДА"] = "WEATHER",
        ["Город"] = "City",
        ["Например, Москва"] = "For example, London",
        ["Широта"] = "Latitude",
        ["Долгота"] = "Longitude",
        ["ПОИСК И ИИ"] = "SEARCH AND AI",
        ["адрес с {0}"] = "URL with {0}",
        ["Спросить ИИ"] = "Ask AI",
        ["ОФОРМЛЕНИЕ"] = "APPEARANCE",
        ["Акцентный цвет"] = "Accent color",
        ["Из обоев"] = "Wallpaper",
        ["Синий"] = "Blue",
        ["Розовый"] = "Pink",
        ["Оранжевый"] = "Orange",
        ["Зелёный"] = "Green",
        ["СИСТЕМА"] = "SYSTEM",
        ["Порт уведомлений {0} нужен для хука Claude Code. Меняется в settings.json, после этого нужен перезапуск."] =
            "Notification port {0} is used by the Claude Code hook. Change it in settings.json, then restart.",
        ["Файл настроек"] = "Settings file",
        ["Ссылка должна начинаться с http или https и содержать {0} там, где вставляется запрос."] =
            "The link must start with http or https and contain {0} where the query goes.",
        ["Координата — число: широта от −90 до 90, долгота от −180 до 180."] =
            "A coordinate is a number: latitude −90 to 90, longitude −180 to 180.",

        // Updates
        ["ОБНОВЛЕНИЯ"] = "UPDATES",
        ["Версия {0}"] = "Version {0}",
        ["Проверять обновления автоматически"] = "Check for updates automatically",
        ["Вкл"] = "On",
        ["Выкл"] = "Off",
        ["Проверить сейчас"] = "Check now",
        ["Обновить до {0}"] = "Update to {0}",
        ["Проверка…"] = "Checking…",
        ["У вас последняя версия"] = "You have the latest version",
        ["Доступна версия {0}"] = "Version {0} is available",
        ["Загрузка…"] = "Downloading…",
        ["Загрузка… {0}%"] = "Downloading… {0}%",
        ["Не удалось проверить обновления"] = "Couldn't check for updates",
        ["Не удалось скачать обновление"] = "Couldn't download the update",
        ["Файл обновления повреждён или подменён"] = "The update file is damaged or was altered",
        ["Установка обновления…"] = "Installing the update…",
        ["Не удалось запустить установщик"] = "Couldn't start the installer",
        ["Доступна новая версия Island"] = "A new version of Island is available",
        ["Версия {0}. Обновить можно в настройках."] = "Version {0}. You can update it in Settings.",

        // Screen recording and game mode
        ["Запись экрана"] = "Record screen",
        ["Видео"] = "Videos",
        ["Что записать?"] = "What to record?",
        ["Без звука, до 30 минут"] = "No sound, up to 30 minutes",
        ["Отмена"] = "Cancel",
        ["Остановить"] = "Stop",
        ["Остановить запись"] = "Stop recording",
        ["Не удалось начать запись"] = "Couldn't start recording",
        ["Не удалось сохранить запись"] = "Couldn't save the recording",
        ["Запись сохранена"] = "Recording saved",
        ["{0} — нажмите, чтобы показать в папке"] = "{0} — click to show it in the folder",
        ["Не хватает места на диске"] = "Not enough disk space",
        ["Нет доступа к папке Видео"] = "No access to the Videos folder",
        ["Достигнут предел {0} мин"] = "{0} min limit reached",
        ["Изменился экран"] = "The display changed",
        ["Не удалось снять экран"] = "Couldn't capture the screen",
        ["Запись слишком короткая"] = "The recording is too short",
        ["РЕЖИМ ДЛЯ ИГР"] = "GAME MODE",
        ["Прятать остров в играх и полноэкранных окнах"] = "Hide the island in games and full-screen windows",
        ["Уведомления, напоминания и таймер покажутся, когда полноэкранное окно закроется или свернётся."] = "Notifications, reminders and the timer show up once the full-screen window is closed or minimized.",
        ["без баннеров"] = "no banners",
        ["Баннеры от этой программы: выключить или включить"] = "Banners from this app: turn off or on",
        ["УВЕДОМЛЕНИЯ"] = "NOTIFICATIONS",
        ["Не беспокоить"] = "Do not disturb",
        ["Баннеры уведомлений Windows и других программ не показываются. Таймер и напоминания работают как обычно."] = "Banners for Windows notifications and other programs are not shown. The timer and reminders work as usual.",
        ["Программы без баннеров не выбраны. Выключить баннеры программы можно на вкладке уведомлений."] = "No apps without banners. You can turn off an app's banners on the notifications tab.",
        ["Без баннеров: {0}"] = "No banners: {0}",
        ["Перезапустить сейчас"] = "Restart now",
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
