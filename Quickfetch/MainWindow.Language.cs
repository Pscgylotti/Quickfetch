using System;
using System.Windows;
using System.Windows.Controls;

namespace Quickfetch;

public partial class MainWindow
{
    private bool _languageReady;
    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_languageReady || LanguageControl.SelectedValue is not string language) return;
        var old = _settings.UiLanguage;
        try
        {
            _settings.UiLanguage = language; _saveSettings(_settings); Loc.SetLanguage(language);
            Loc.RefreshTree(this); UpdateSortLabels(); UpdateResults();
            if (_tray?.ContextMenuStrip is { } menu) foreach (System.Windows.Forms.ToolStripItem item in menu.Items) item.Text = Loc.Existing(item.Text ?? "");
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { _settings.UiLanguage = old; LanguageControl.SelectedValue = old; SetMessage(ex.Message); }
    }
}
