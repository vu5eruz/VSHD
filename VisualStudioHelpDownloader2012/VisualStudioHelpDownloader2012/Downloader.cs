﻿namespace VisualStudioHelpDownloader2012
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Globalization;
	using System.IO;
	using System.Linq;
	using System.Net;
	using System.Reflection;
	using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Forms;
	using System.Xml.Linq;
	using System.Xml;

	/// <summary>
	///		Contains the arguments of a BooksDownloadStatusChanged event handler.
	/// </summary>
	internal sealed class BooksDownloadStatusChangedEventArgs
    {
		/// <summary>
		/// File name (not including path) of the file being downloaded.
		/// </summary>
		public string Filename { get; }

		/// <summary>
		/// Percent range [0,100] of the file downloaded.
		/// </summary>
		public int Percent { get; }

		/// <summary>
		/// Number of bytes downloaded or -1 if download has not been started.
		/// </summary>
		public long BytesDownloaded { get; }

		/// <summary>
		/// Number of bytes to download or -1 if download has not been started.
		/// </summary>
		public long BytesToDownload { get; }

		/// <summary>
		/// Constructor.
		/// </summary>
		public BooksDownloadStatusChangedEventArgs(String filename, int percent, long bytesDownloaded, long bytesToDownload)
        {
			Filename = filename;
			Percent = percent;
			BytesDownloaded = bytesDownloaded;
			BytesToDownload = bytesToDownload;
        }
    }

	/// <summary>
	///     Class to perfom the downloading of the MSDN book information and the books themselves
	/// </summary>
	internal sealed class Downloader : IDisposable
	{	
		/// <summary>
		/// Event to notify a change during books download such download started, download progress,
		/// and download finish.
		/// </summary>
		public event EventHandler<BooksDownloadStatusChangedEventArgs> BooksDownloadStatusChanged;
		
		/// <summary>
		/// The http client used for downloading
		/// </summary>
		private WebClient client = new WebClient();

		/// <summary>
		/// Initializes a new instance of the <see cref="Downloader"/> class.
		/// </summary>
		/// <exception cref="XmlException">
		/// If the settings cannot be loaded
		/// </exception>
		/// <exception cref="InvalidOperationException">
		/// If the data cannot be processed
		/// </exception>
		public Downloader()
		{
			client.BaseAddress = "https://services.mtps.microsoft.com/serviceapi/";

			string directory = Path.GetDirectoryName( Application.ExecutablePath );
			if ( directory != null )
			{
				string settingsFile = Path.Combine(
					directory,
					string.Format( CultureInfo.InvariantCulture, "{0}.xml", Assembly.GetEntryAssembly().GetName().Name ) );

				if ( File.Exists( settingsFile ) )
				{
					XElement element = XDocument.Load( settingsFile ).Root;
					if ( element != null )
					{
						element = element.Elements().Single( x => x.Name.LocalName?.Equals( "proxy", StringComparison.OrdinalIgnoreCase ) ?? false );
						WebProxy proxy = new WebProxy( element.Attributes().Single( x => x.Name.LocalName?.Equals( "address", StringComparison.OrdinalIgnoreCase ) ?? false ).Value )
						{
							Credentials =
								new NetworkCredential(
								element.Attributes().Single( x => x.Name.LocalName?.Equals( "login", StringComparison.OrdinalIgnoreCase ) ?? false ).Value,
								element.Attributes().Single( x => x.Name.LocalName?.Equals( "password", StringComparison.OrdinalIgnoreCase ) ?? false ).Value,
								element.Attributes().Single( x => x.Name.LocalName?.Equals( "domain", StringComparison.OrdinalIgnoreCase ) ?? false ).Value )
						};

						client.Proxy = proxy;
					}
					else
					{
						throw new XmlException( "Missing root element" );
					}
				}				
			}
		}

		/// <summary>
		/// Finalizes an instance of the <see cref="Downloader"/> class. 
		/// </summary>
		~Downloader()
		{
			Dispose( false );
		}

		/// <summary>
		/// Check the current caching status of the packages so that the required downloads can be
		/// determined
		/// </summary>
		/// <param name="bookGroups">
		/// The collection of bookGroups to check the packages for
		/// </param>
		/// <param name="cachePath">
		/// The directory where the packages are locally cached
		/// </param>
		/// <exception cref="ArgumentNullException">
		/// If bookGroups or cachePath are null
		/// </exception>
		public static void CheckPackagesStates( ICollection<BookGroup> bookGroups, string cachePath )
		{
			if ( bookGroups == null )
			{
				throw new ArgumentNullException( "bookGroups" );
			}

			if ( cachePath == null )
			{
				throw new ArgumentNullException( "cachePath" );
			}

			foreach ( BookGroup bookGroup in bookGroups )
			{
				foreach ( Book book in bookGroup.Books )
				{
					foreach ( Package package in book.Packages )
					{
						string packagePath = Path.Combine( cachePath, "Packages", package.Name + ".cab" );
						FileInfo packageFile = new FileInfo( packagePath );
						if ( packageFile.Exists )
						{
							if ( packageFile.LastWriteTime == package.LastModified && packageFile.Length == package.Size )
							{
								package.State = PackageState.OutOfDate;
							}
							else
							{
								package.State = PackageState.Ready;
							}
						}
						else
						{
							package.State = PackageState.NotDownloaded;
						}
					}
				}
			}
		}

		/// <summary>
		/// The dispose.
		/// </summary>
		public void Dispose()
		{
			Dispose( true );
			GC.SuppressFinalize( this );
		}

		/// <summary>
		/// Retrieves a collection of locales available to download the help for
		/// </summary>
		/// <returns>
		/// Collection of Locales available
		/// </returns>
		/// <exception cref="WebException">
		/// If the data cannot be downloaded
		/// </exception>
		/// <exception cref="XmlException">
		/// If the data cannot be processed
		/// </exception>
		/// <exception cref="InvalidOperationException">
		/// If the data cannot be processed
		/// </exception>
		public async Task<ICollection<Locale>> LoadAvailableLocalesAsync( string vsVersion )
		{
			string catalogPath = string.Format("catalogs/{0}", vsVersion);
			Debug.Print("Downloading locales list from {0}{1}", client.BaseAddress, catalogPath);
			ICollection<Locale> locales = HelpIndexManager.LoadLocales( await client.DownloadDataTaskAsync( catalogPath ) );
			return locales;
		}

		/// <summary>
		/// Download information about the available books for the selected locale
		/// </summary>
		/// <param name="path">
		/// The relative path to the book catalog download location
		/// </param>
		/// <returns>
		/// Collection of available bookGroups
		/// </returns>
		/// <exception cref="NullReferenceException">
		/// If path is null or empty
		/// </exception>
		/// <exception cref="WebException">
		/// If the data cannot be downloaded
		/// </exception>
		/// <exception cref="XmlException">
		/// If the data cannot be processed
		/// </exception>
		/// <exception cref="InvalidOperationException">
		/// If the data cannot be processed
		/// </exception>
		public async Task<ICollection<BookGroup>> LoadBooksInformationAsync( string path )
		{
			if ( path == null )
			{
				throw new ArgumentNullException( "path" );
			}

			Debug.Print("Downloading books list from {0}{1}", client.BaseAddress, path);
			return HelpIndexManager.LoadBooks( await client.DownloadDataTaskAsync( path ) );
		}

		/// <summary>
		/// Download the requested books and create the appropriate index files for MSDN HelpViewer
		/// </summary>
		/// <param name="bookGroups">
		/// The collection of bookGroups to with the books to download indicated by the Book.Wanted
		/// property
		/// </param>
		/// <param name="cachePath">
		/// The path where the downloaded books are cached
		/// </param>
		/// <param name="globalProgress">
		/// Interface used to report the percentage progress back to the GUI
		/// </param>
		/// <exception cref="ArgumentNullException">
		/// If any of the parameters are null
		/// </exception>
		/// <exception cref="WebException">
		/// If the data cannot be downloaded
		/// </exception>
		/// <exception cref="XmlException">
		/// If the data cannot be processed
		/// </exception>
		/// <exception cref="IOException">
		/// If there was a problem reading or writing to the cache directory
		/// </exception>
		/// <exception cref="UnauthorizedAccessException">
		/// If the user does not have permission to write to the cache directory
		/// </exception>
		/// <exception cref="InvalidOperationException">
		/// If the data cannot be processed
		/// </exception>
		public async Task DownloadBooksAsync( ICollection<BookGroup> bookGroups, string cachePath, IProgress<int> globalProgress )
		{
			if ( bookGroups == null )
			{
				throw new ArgumentNullException( "bookGroups" );
			}

			if ( cachePath == null )
			{
				throw new ArgumentNullException( "cachePath" );
			}

			if ( cachePath == null )
			{
				throw new ArgumentNullException( "progress" );
			}

			// Create cachePath
			string targetDirectory = Path.Combine( cachePath, "Packages" );

			if ( !Directory.Exists( targetDirectory ) )
			{
				Directory.CreateDirectory( targetDirectory );
			}

			// Cleanup index files
			Directory.GetFiles( cachePath, "*.msha" ).ForEach( File.Delete );
			Directory.GetFiles( cachePath, "*.xml" ).ForEach( File.Delete );

			// Creating setup indexes
			File.WriteAllText(
				Path.Combine( cachePath, "HelpContentSetup.msha" ), HelpIndexManager.CreateSetupIndex( bookGroups ), Encoding.UTF8 );

			// Create list of unique packages for possible download and write the book group and
			// book index files
			Dictionary<string, Package> packages = new Dictionary<string, Package>();
			foreach ( BookGroup bookGroup in bookGroups )
			{
				File.WriteAllText(
					Path.Combine( cachePath, bookGroup.CreateFileName() ), 
					HelpIndexManager.CreateBookGroupBooksIndex( bookGroup ), 
					Encoding.UTF8 );
				Debug.Print( "BookGroup: {0}", bookGroup.Name );
				foreach ( Book book in bookGroup.Books )
				{
					if ( book.Wanted )
					{
						Debug.Print( "   Book: {0}", book.Name );
						File.WriteAllText(
							Path.Combine( cachePath, book.CreateFileName() ), 
							HelpIndexManager.CreateBookPackagesIndex( bookGroup, book ), 
							Encoding.UTF8 );
						foreach ( Package package in book.Packages )
						{
							string name = package.Name.ToUpperInvariant();
							Debug.Print( "      Package: {0}", name );
							if ( !packages.ContainsKey( name ) )
							{
								packages.Add( name, package );
							}
						}
					}
				}
			}

			// Cleanup old files
			foreach ( string file in Directory.GetFiles( targetDirectory, "*.cab" ) )
			{
				string fileName = Path.GetFileNameWithoutExtension( file );
				if ( !string.IsNullOrEmpty( fileName ) )
				{
					fileName = fileName.ToUpperInvariant();
					if ( !packages.ContainsKey( fileName ) )
					{
						File.Delete( file );
					}
				}
			}

			// Download the packages
			int packagesCountCurrent = 0;
			client.BaseAddress = "https://packages.mtps.microsoft.com/";
			foreach ( Package package in packages.Values )
			{
				string packageFileName = package.CreateFileName();
				string targetFileName = Path.Combine( targetDirectory, packageFileName );
				if ( package.State == PackageState.NotDownloaded || package.State == PackageState.OutOfDate )
				{
					Debug.Print( "         Downloading : '{0}' to '{1}'", package.Link, targetFileName );
					
					OnBooksDownloadStatusChanged(packageFileName, 0);

                    client.DownloadProgressChanged += (object sender, DownloadProgressChangedEventArgs e) =>
					{
						// TODO(vu5eruz): Prevent the event being raised hundreds of times per second
						
						OnBooksDownloadStatusChanged(packageFileName, e.ProgressPercentage, e.BytesReceived, e.TotalBytesToReceive);
					};

					await client.DownloadFileTaskAsync( package.Link, targetFileName );

					OnBooksDownloadStatusChanged(packageFileName, 100);

					if (AuthenticodeTools.IsTrusted(targetFileName))
					{
						File.SetCreationTime(targetFileName, package.LastModified);
						File.SetLastAccessTime(targetFileName, package.LastModified);
						File.SetLastWriteTime(targetFileName, package.LastModified);
					}
					else
					{
						Debug.Print("The signature on '{0}' is not valid - deleting");
						File.Delete(targetFileName);
						
						throw new InvalidDataException($"The signature on '{targetFileName}' is not valid - deleting");
					}
				}

				globalProgress.Report( 100 * ++packagesCountCurrent / packages.Count );
			}
		}

        /// <summary>
        /// Raise the BooksDownloadStatusChanged event.
        /// </summary>
        /// <param name="e">The handler arguments.</param>
        private void OnBooksDownloadStatusChanged(string FileName, int Percent, long bytesDownloaded = -1, long bytesToDownload = -1)
        {
			var handler = BooksDownloadStatusChanged;

			handler?.Invoke(this, new BooksDownloadStatusChangedEventArgs(FileName, Percent, bytesDownloaded, bytesToDownload));
		}

		/// <summary>
		/// Standard IDispose pattern
		/// </summary>
		/// <param name="disposing">
		/// true if called by Dispose, false if called from destructor
		/// </param>
		private void Dispose( bool disposing )
		{
			if ( disposing )
			{
				if ( client != null )
				{
					client.Dispose();
					client = null;
				}
			}
		}
	}
}
