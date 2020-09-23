﻿namespace VisualStudioHelpDownloader2012
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.IO;
	using System.Net;
	using System.Threading.Tasks;
	using System.Windows.Forms;
	using System.Diagnostics;

	/// <summary>
	///     Main application form.
	/// </summary>
	internal sealed partial class MainForm : Form, IProgress<int>
	{
		/// <summary>
		/// The products.
		/// </summary>
		private ICollection<BookGroup> products;

		/// <summary>
		/// Initializes a new instance of the <see cref="MainForm"/> class.
		/// </summary>
		public MainForm()
		{
			InitializeComponent();

			Text = Application.ProductName;
			products = new List<BookGroup>();
			startupTip.Visible = false;
			cacheDirectory.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "MSDN Library");
		}

		/// <summary>
		/// Reports a progress update.
		/// </summary>
		/// <param name="value">
		/// The value of the updated progress. (percentage complete)
		/// </param>
		public void Report(int value)
		{
			Invoke(new MethodInvoker(
				delegate
				{
					downloadProgress.Value = value;
				}
			));
		}

		/// <summary>
		/// Handler for the BooksDownloadStatusChanged event.
		/// </summary>
		/// <param name="sender">Unused.</param>
		/// <param name="e">Used.</param>
		private void OnBooksDownloadStatusChanged(object sender, BooksDownloadStatusChangedEventArgs e)
        {
			Invoke(new MethodInvoker(
				delegate
				{
					if (e.BytesDownloaded > 0)
                    {
						statusStripStatus.Text = $"Downloading {e.Filename} ({e.BytesDownloaded} / {e.BytesToDownload}) ...";
					}
                    else
                    {
						statusStripStatus.Text = $"Downloading {e.Filename} ...";
					}
					
					statusStripProgress.Value = e.Percent;
				}
			));
		}

		/// <summary>
		/// Called when the form is loaded. Start retrieving the list of available
		/// languages in the background.
		/// </summary>
		/// <param name="e">
		/// The parameter is not used.
		/// </param>
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			loadingBooksTip.Visible = false;
			startupTip.Visible = true;

			UpdateStatus();
		}

		/// <summary>
		/// Updates the text in the status strip.
		/// </summary>
		private void UpdateStatus()
		{
			statusStripProgress.Value = e.Percent;

			if (cacheDirectory.Text == "")
			{
				statusStripStatus.Text = "Missing Output Directory";
			}
			else if (vsVersion.SelectedIndex == -1)
			{
				statusStripStatus.Text = "Select Visual Studio version";
			}
			else if (languageSelection.SelectedIndex == -1)
			{
				statusStripStatus.Text = "Select language";
			}
			else if (booksList.Items.Count == 0)
			{
				statusStripStatus.Text = "Load books";
			}
			else
			{
				statusStripStatus.Text = "Select books and hit download";
			}
		}

		/// <summary>
		/// Called to update the available locales for the selected version of visual studio
		/// </summary>
		private async Task UpdateLocalesAsync()
		{
			if ( vsVersion.SelectedIndex == -1 )
			{
				return;
			}

			string[] vsVersions = { "visualstudio11", "visualstudio12", "dev14", "dev15" };
			string version = vsVersions[vsVersion.SelectedIndex];
			startupTip.Visible = false;
			SetBusyState();
			languageSelection.Items.Clear();
			downloadProgress.Style = ProgressBarStyle.Marquee;

			try
			{
				using (Downloader downloader = new Downloader())
				{
					statusStripStatus.Text = "Updating locales ...";

					(await downloader.LoadAvailableLocalesAsync(version)).ForEach(x => languageSelection.Items.Add(x));
				}

				UpdateStatus();
			}
			catch ( Exception ex )
			{
				statusStripStatus.Text = "Locales updating failed";

				MessageBox.Show(
					$"Locales update failed - {ex.Message}",
					Application.ProductName,
					MessageBoxButtons.OK,
					MessageBoxIcon.Error,
					MessageBoxDefaultButton.Button1,
					0);
			}
			finally
			{
				ClearBusyState();
				startupTip.Visible = true;
			}
		}

		/// <summary>
		/// Called when the download books button is clicked. Start downloading in a background thread
		/// </summary>
		/// <param name="sender">
		/// The parameter is not used.
		/// </param>
		/// <param name="e">
		/// The parameter is not used.
		/// </param>
		private async void DownloadBooksClick( object sender, EventArgs e )
		{
			SetBusyState();
			downloadProgress.Style = ProgressBarStyle.Continuous;
			downloadProgress.Value = 0;

			try
			{
				using (Downloader downloader = new Downloader())
				{
					statusStripStatus.Text = "Initializing books download ...";

					downloader.BooksDownloadStatusChanged += OnBooksDownloadStatusChanged;

					await downloader.DownloadBooksAsync(products, cacheDirectory.Text, this);

					MessageBox.Show(
						"Download completed successfully",
						Application.ProductName,
						MessageBoxButtons.OK,
						MessageBoxIcon.Information,
						MessageBoxDefaultButton.Button1,
						0);
				}

				UpdateStatus();
			}
			catch ( Exception ex )
			{
				statusStripStatus.Text = "Books download failed";

				MessageBox.Show(
					$"Download failed - {ex.Message}",
					Application.ProductName,
					MessageBoxButtons.OK,
					MessageBoxIcon.Error,
					MessageBoxDefaultButton.Button1,
					0);
			}
			finally
			{
				ClearBusyState();
				DisplayBooks();
				downloadProgress.Value = 0;
			}

		}

		/// <summary>
		/// Called when the load books button is clicked. Load the list of available books for the selected
		/// language
		/// </summary>
		/// <param name="sender">
		/// The parameter is not used.
		/// </param>
		/// <param name="e">
		/// The parameter is not used.
		/// </param>
		private async void LoadBooksClick( object sender, EventArgs e )
		{
			string path = ((Locale)languageSelection.SelectedItem).CatalogLink;

			SetBusyState();
			downloadProgress.Style = ProgressBarStyle.Marquee;
			startupTip.Visible = false;

			try
			{
				using (Downloader downloader = new Downloader())
				{
					statusStripStatus.Text = "Loading books ...";

					products = await downloader.LoadBooksInformationAsync(path);
					DisplayBooks();
				}

				UpdateStatus();
			}
			catch ( Exception ex )
			{
				statusStripStatus.Text = "Books loading failed";

				MessageBox.Show(
					$"Failed to retrieve book information - {ex.Message}",
					Application.ProductName,
					MessageBoxButtons.OK,
					MessageBoxIcon.Error,
					MessageBoxDefaultButton.Button1,
					0);
			}
			finally
			{
				ClearBusyState();
			}
		}

		/// <summary>
		/// Enable/disable, hide/show controls for when the program is not busy 
		/// </summary>
		private void ClearBusyState()
		{
			vsVersion.Enabled = true;
			languageSelection.Enabled = languageSelection.Items.Count > 0;
			loadBooks.Enabled = languageSelection.Items.Count > 0;
			downloadBooks.Enabled = (booksList.Items.Count > 0) && !string.IsNullOrEmpty( cacheDirectory.Text );
			browseDirectory.Enabled = true;
			downloadProgress.Style = ProgressBarStyle.Continuous;
			startupTip.Visible = false;
			loadingBooksTip.Visible = false;
			booksList.Enabled = true;
		}

		/// <summary>
		/// Enable/disable, hide/show controls for when the program is busy 
		/// </summary>
		private void SetBusyState()
		{
			vsVersion.Enabled = false;
			languageSelection.Enabled = false;
			loadBooks.Enabled = false;
			downloadBooks.Enabled = false;
			browseDirectory.Enabled = false;
			booksList.Enabled = false;
			loadingBooksTip.Visible = true;
		}

		/// <summary>
		/// Populate the list view control with the books available for download
		/// </summary>
		private void DisplayBooks()
		{
			booksList.Items.Clear();
			if ( !string.IsNullOrEmpty( cacheDirectory.Text ) )
			{
				Downloader.CheckPackagesStates( products, cacheDirectory.Text );
			}

			Dictionary<string, ListViewGroup> groups = new Dictionary<string, ListViewGroup>();
			foreach ( BookGroup product in products )
			{
				foreach ( Book book in product.Books )
				{
					// Calculate some details about any prospective download
					long totalSize = 0;
					long downloadSize = 0;
					int packagesOutOfDate = 0;
					int packagesCached = 0;
					foreach ( Package package in book.Packages )
					{
						totalSize += package.Size;
						if ( package.State != PackageState.Ready )
						{
							downloadSize += package.Size;
							packagesOutOfDate++;
						}

						if ( package.State != PackageState.NotDownloaded )
						{
							packagesCached++;
						}
					}

					// Make sure the groups aren't duplicated
					ListViewGroup itemGroup;
					if ( groups.ContainsKey( book.Category ) )
					{
						itemGroup = groups[book.Category];
					}
					else
					{
						itemGroup = booksList.Groups.Add( book.Category, book.Category );
						groups.Add( book.Category, itemGroup );
					}

					ListViewItem item = booksList.Items.Add( book.Name );
					item.SubItems.Add( (totalSize / 1000000).ToString( "F1", CultureInfo.CurrentCulture ) );
					item.SubItems.Add( book.Packages.Count.ToString( CultureInfo.CurrentCulture ) );
					item.SubItems.Add( (downloadSize / 1000000).ToString( "F1", CultureInfo.CurrentCulture ) );
					item.SubItems.Add( packagesOutOfDate.ToString( CultureInfo.CurrentCulture ) );
					item.ToolTipText = book.Description;
					item.Checked = packagesCached > 1;
					book.Wanted = item.Checked;
					item.Tag = book;
					item.Group = itemGroup;
				}
			}
		}

		/// <summary>
		/// Called when the browse for directory button is clicked. Show an folder browser to allow the
		/// user to select a directory to store the cached file in
		/// </summary>
		/// <param name="sender">
		/// The parameter is not used.
		/// </param>
		/// <param name="e">
		/// The parameter is not used.
		/// </param>
		private void BrowseDirectoryClick( object sender, EventArgs e )
		{
			using ( FolderBrowserDialog dialog = new FolderBrowserDialog() )
			{
				dialog.RootFolder = Environment.SpecialFolder.MyComputer;
				dialog.SelectedPath = cacheDirectory.Text;
				dialog.ShowNewFolderButton = true;
				dialog.Description = "Select local cache folder to store selected MSDN Library books";

				if ( DialogResult.OK == dialog.ShowDialog( this ) )
				{
					cacheDirectory.Text = dialog.SelectedPath;
					downloadBooks.Enabled = (booksList.Items.Count > 0) && !string.IsNullOrEmpty( cacheDirectory.Text );
					DisplayBooks();
				}
			}
		}

		/// <summary>
		/// Called when the checkbox of one of the listview items is checked or unchecked. Mark the associated book state
		/// </summary>
		/// <param name="sender">
		/// The parameter is not used.
		/// </param>
		/// <param name="e">
		/// Details about the item checked/unchecked
		/// </param>
		private void BooksListItemChecked( object sender, ItemCheckedEventArgs e )
		{
            if (e.Item.Tag is Book book)
            {
                book.Wanted = e.Item.Checked;
            }
        }

		/// <summary>
		/// Called when the language combobox selection is changed. Clear the
		/// currently list of available books and reshow the instruction.
		/// </summary>
		/// <param name="sender">
		/// The parameter is not used.
		/// </param>
		/// <param name="e">
		/// The parameter is not used.
		/// </param>
		private void BookOptionsChanged( object sender, EventArgs e )
		{
			booksList.Items.Clear();
			downloadBooks.Enabled = false;
			startupTip.Visible = true;
			UpdateStatus();
		}

		/// <summary>
		/// Called when the visual studio version combobox selection is changed. Clear the
		/// currently list of available books and reshow the instruction.
		/// </summary>
		/// <param name="sender">
		/// The parameter is not used.
		/// </param>
		/// <param name="e">
		/// The parameter is not used.
		/// </param>
		private async void VsVersionChanged(object sender, EventArgs e)
		{
			booksList.Items.Clear();
			languageSelection.Items.Clear();
			languageSelection.SelectedItem = -1;
			UpdateStatus();
			await UpdateLocalesAsync();
		}
	}
}
