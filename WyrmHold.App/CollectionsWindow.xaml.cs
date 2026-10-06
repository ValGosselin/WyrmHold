using System.Windows;
using System.Windows.Controls;
using Wyrmhold.Core;

namespace WyrmHold.App;

public partial class CollectionsWindow : Window
{
    private readonly LibraryService _library;

    public CollectionsWindow(LibraryService library)
    {
        InitializeComponent();
        _library = library;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ReloadList();
        NameBox.Focus();
    }

    private void ReloadList(long? collectionIdToSelect = null)
    {
        List<GameCollection> collections = _library.LoadCollections();
        CollectionsList.ItemsSource = collections;

        if (collectionIdToSelect is not null)
        {
            CollectionsList.SelectedItem = collections.FirstOrDefault(c => c.Id == collectionIdToSelect);
        }
    }

    private void CollectionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Sélectionner une collection met son nom dans la zone de texte, prêt à être modifié.
        if (CollectionsList.SelectedItem is GameCollection collection)
        {
            NameBox.Text = collection.Name;
        }
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _library.CreateCollection(NameBox.Text);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Wyrmhold");
            return;
        }

        NameBox.Clear();
        ReloadList();
        NameBox.Focus();
    }

    private void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionsList.SelectedItem is not GameCollection collection)
        {
            MessageBox.Show("Sélectionne d'abord la collection à renommer, puis écris son nouveau nom.", "Wyrmhold");
            return;
        }

        try
        {
            _library.RenameCollection(collection, NameBox.Text);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Wyrmhold");
            return;
        }

        ReloadList(collection.Id);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionsList.SelectedItem is not GameCollection collection)
        {
            MessageBox.Show("Sélectionne d'abord la collection à supprimer.", "Wyrmhold");
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            $"Supprimer la collection « {collection.Name} » ? Tes jeux ne sont pas supprimés, "
            + "ils sont seulement retirés de cette collection.",
            "Wyrmhold",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _library.DeleteCollection(collection);
        NameBox.Clear();
        ReloadList();
    }
}