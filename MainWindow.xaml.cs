// CodeSource2Txt/MainWindow.xaml.cs

using System.IO;
using System.Text;
using System.Windows;
using Forms = System.Windows.Forms;

namespace CodeSource2Txt;

public partial class MainWindow : Window
{
    private string _dernierFichierGenere;

    private static readonly HashSet<string> DossiersExclus =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "bin",
        "obj",
        ".vs",
        ".git",
        "packages",
        "node_modules",
        "TestResults"
        };

    private static readonly HashSet<string> ExtensionsTexte =
        new(StringComparer.OrdinalIgnoreCase)
        {
        ".cs",
        ".xaml",
        ".csproj",
        ".sln",
        ".json",
        ".config",
        ".resx",
        ".xml",
        ".txt",
        ".md",
        ".sql",
        ".css",
        ".js",
        ".ts",
        ".html",
        ".razor",
        ".props",
        ".targets",
        ".editorconfig"
        };

    private const long TailleMaximaleFichier = 2 * 1024 * 1024;

    public MainWindow()
    {
        InitializeComponent();
        AjouterJournal("Application prête.");
        AjouterJournal("Sélectionnez le dossier du projet source.");
    }

    private void ChoisirSource_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Sélectionnez le dossier du projet C# WPF",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            TxtSource.Text = dialog.SelectedPath;
            AjouterJournal($"Dossier source sélectionné : " + $"{dialog.SelectedPath}");
        }
    }


    private async void Concatener_Click(object sender, RoutedEventArgs e)
    {
        string dossierSource = TxtSource.Text.Trim();

        if (!VerifierDossierSource(dossierSource))        
            return;        

        System.IO.DirectoryInfo di = new(dossierSource);
        string n = di.Name + ".txt";

        using var dialog = new Forms.SaveFileDialog
        {
            Title = "Enregistrer le projet concaténé",

            Filter = "Fichier texte (*.txt)|*.txt|" +
                "Fichier Markdown (*.md)|*.md|" +
                "Tous les fichiers (*.*)|*.*",

            FileName = n,
            InitialDirectory = di.Parent.FullName,
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
            return;

        try
        {
            ChangerEtatInterface(false);
            AjouterJournal("Recherche des fichiers texte...");

            List<string> fichiers =
                await Task.Run(() => RecupererFichiers(dossierSource, inclureTousLesFichiers: false));

            if (fichiers.Count == 0)
            {
                AfficherInformation("Aucun fichier texte compatible n'a été trouvé.");
                return;
            }

            Progression.Minimum = 0;
            Progression.Maximum = fichiers.Count;
            Progression.Value = 0;

            var progression = new Progress<int>(nombre =>{Progression.Value = nombre;});

            string contenu = await Task.Run(() => ConstruireContenuConcatene(dossierSource, fichiers, progression));

            await File.WriteAllTextAsync(dialog.FileName, contenu, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _dernierFichierGenere = dialog.FileName;
            BtnOuvrirDossier.IsEnabled = true;

            AjouterJournal(
                $"Fichier concaténé créé : " +
                $"{dialog.FileName}");

            AfficherInformation(
                "Le fichier concaténé a été créé avec succès." +
                Environment.NewLine +
                Environment.NewLine +
                "Emplacement :" +
                Environment.NewLine +
                dialog.FileName);
        }
        catch (Exception ex)
        {
            AfficherErreur(
                "Une erreur est survenue pendant la " +
                "concaténation :" +
                Environment.NewLine +
                Environment.NewLine +
                ex.Message);
        }
        finally
        {
            ChangerEtatInterface(true);
        }
    }

    private void OuvrirDossier_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_dernierFichierGenere) || !File.Exists(_dernierFichierGenere))
        {
            AfficherErreur("Le fichier généré n'existe pas ou n'a pas encore été créé.");
            return;
        }

        try
        {
            string argument = $"/select,\"{_dernierFichierGenere}\"";
            System.Diagnostics.Process.Start("explorer.exe", argument);
        }
        catch (Exception ex)
        {
            AfficherErreur(
                "Impossible d'ouvrir l'explorateur :" +
                Environment.NewLine +
                Environment.NewLine +
                ex.Message);
        }
    }

    private static List<string> RecupererFichiers(
        string dossierSource,
        bool inclureTousLesFichiers)
    {
        return Directory
            .EnumerateFiles(dossierSource, "*", SearchOption.AllDirectories)
            .Where(fichier => !EstDansUnDossierExclus(dossierSource, fichier))
            .Where(fichier => inclureTousLesFichiers || ExtensionsTexte.Contains(Path.GetExtension(fichier)))
            .OrderBy(fichier => Path.GetRelativePath(dossierSource, fichier))
            .ToList();
    }

    private static string ConstruireContenuConcatene(string dossierSource, List<string> fichiers, IProgress<int> progression)
    {
        var resultat = new StringBuilder();

        resultat.AppendLine("# CONTENU CONCATÉNÉ DU PROJET C# WPF");
        resultat.AppendLine();
        resultat.AppendLine($"# Nombre de fichiers : {fichiers.Count}");
        resultat.AppendLine($"# Date de génération : " + $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        resultat.AppendLine();
        int compteur = 0;

        foreach (string fichier in fichiers)
        {
            string cheminRelatif = ConvertirCheminRelatif(Path.GetRelativePath(dossierSource, fichier));
            resultat.AppendLine($"===== DEBUT DU FICHIER : {cheminRelatif} =====");
            resultat.AppendLine();
            string entete = GenererEnteteFichier(cheminRelatif);
            resultat.AppendLine(entete);
            resultat.AppendLine();

            try
            {
                FileInfo informations = new(fichier);

                if (informations.Length > TailleMaximaleFichier)                
                    resultat.AppendLine("[FICHIER IGNORÉ : taille supérieure à 2 Mo]");                
                else
                {
                    string contenu = LireFichierTexte(fichier);
                    resultat.AppendLine(contenu);
                }
            }
            catch (Exception ex)
            {
                resultat.AppendLine($"[ERREUR DE LECTURE : {ex.Message}]");
            }

            resultat.AppendLine();
            resultat.AppendLine($"===== FIN DU FICHIER : {cheminRelatif} =====");
            resultat.AppendLine();

            compteur++;
            progression.Report(compteur);
        }

        return resultat.ToString();
    }

    private static string GenererEnteteFichier(string cheminRelatif)
    {
        string extension = Path.GetExtension(cheminRelatif);
        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))        
            return $"// {cheminRelatif}";       
        return $"<!-- {cheminRelatif} -->";
    }

    private static string LireFichierTexte(string fichier)
    {
        return File.ReadAllText(fichier, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static bool EstDansUnDossierExclus(string dossierSource, string cheminFichier)
    {
        string cheminRelatif = Path.GetRelativePath(dossierSource, cheminFichier);
        string[] parties = cheminRelatif.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parties.Any(partie => DossiersExclus.Contains(partie));
    }

    private static bool DossiersIdentiquesOuImbriques(
        string dossierSource,
        string dossierDestination)
    {
        string source =
            NormaliserChemin(dossierSource);

        string destination =
            NormaliserChemin(dossierDestination);

        return destination.Equals(
                   source,
                   StringComparison.OrdinalIgnoreCase)
               ||
               destination.StartsWith(
                   source + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string NormaliserChemin(string chemin)
    {
        return Path.GetFullPath(chemin).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ConvertirCheminRelatif(string chemin)
    {
        return chemin.Replace(Path.DirectorySeparatorChar, '/');
    }

    private bool VerifierDossierSource(string dossierSource)
    {
        if (string.IsNullOrWhiteSpace(dossierSource))
        {
            AfficherErreur("Veuillez sélectionner un dossier source.");
            return false;
        }

        if (!Directory.Exists(dossierSource))
        {
            AfficherErreur("Le dossier source sélectionné n'existe pas.");
            return false;
        }

        return true;
    }

    private void AjouterJournal(string message)
    {
        TxtJournal.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        TxtJournal.ScrollToEnd();
    }

    private void ChangerEtatInterface(bool actif)
    {
        BtnConcatener.IsEnabled = actif;
        BtnOuvrirDossier.IsEnabled = actif && !string.IsNullOrEmpty(_dernierFichierGenere);
    }

    private static void AfficherInformation(string message)
    {
        System.Windows.MessageBox.Show(message, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void AfficherErreur(string message)
    {
        System.Windows.MessageBox.Show(message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}