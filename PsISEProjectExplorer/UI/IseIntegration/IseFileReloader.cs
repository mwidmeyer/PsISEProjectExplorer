﻿using Microsoft.PowerShell.Host.ISE;
using PsISEProjectExplorer.Model;
using PsISEProjectExplorer.Services;
using PsISEProjectExplorer.UI.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PsISEProjectExplorer.UI.IseIntegration
{
    public class IseFileReloader
    {

        private IseIntegrator IseIntegrator { get; set; }

        private IDictionary<string, IseFileWatcher> IseFileWatchers { get; set; }

        private ISet<string> PathsToIgnore { get; set; }

        private FileSystemChangeNotifier FileSystemChangeNotifier { get; set; }

        public IseFileReloader(IseIntegrator iseIntegrator)
        {
            this.IseIntegrator = iseIntegrator;
            this.IseFileWatchers = new Dictionary<string, IseFileWatcher>();
            this.PathsToIgnore = new HashSet<string>();
            this.FileSystemChangeNotifier = new FileSystemChangeNotifier();
            this.FileSystemChangeNotifier.FileSystemChanged += OnIseFileChangedBatch;
            this.IseIntegrator.AttachFileCollectionChangedHandler(this.OnIseFilesCollectionChanged);
        }

        private void OnIseFilesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (ISEFile oldItem in e.OldItems)
                {
                    if (e.NewItems == null || !e.NewItems.Contains(oldItem))
                    {
                        var path = oldItem.FullPath;
                        if (this.IseFileWatchers.ContainsKey(path))
                        {
                            this.IseFileWatchers[path].StopWatching();
                            this.IseFileWatchers.Remove(path);
                        }
                    }
                }
            }

            if (e.NewItems != null)
            {
                foreach (ISEFile newItem in e.NewItems)
                {
                    if (e.OldItems == null || !e.OldItems.Contains(newItem))
                    {
                        var path = newItem.FullPath;
                        if (!this.IseFileWatchers.ContainsKey(path))
                        {
                            this.IseFileWatchers.Add(path, new IseFileWatcher(this.FileSystemChangeNotifier, path));
                        }
                        newItem.PropertyChanged += OnIseFilePropertyChanged;
                    }
                }
            }
        }

        private void OnIseFilePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            ISEFile file = sender as ISEFile;
            if (file == null || e.PropertyName != "IsSaved")
            {
                return;
            }
            lock (this.PathsToIgnore)
            {
                if (file.IsSaved)
                {
                    this.PathsToIgnore.Add(file.FullPath);
                }
                else
                {
                    this.PathsToIgnore.Remove(file.FullPath);
                }
            }
        }

        private void OnIseFileChangedBatch(object sender, FileSystemChangedInfo changedInfo)
        {
            lock (this.PathsToIgnore)
            {
                foreach (var changePoolEntry in changedInfo.PathsChanged)
                {
                    var pathChanged = changePoolEntry.PathChanged;
                    if (this.PathsToIgnore.Contains(pathChanged))
                    {
                        this.PathsToIgnore.Remove(pathChanged);
                    }
                    else
                    {
                        this.ReloadFileOpenInIse(pathChanged);
                    }
                }
            }
        }

        private void ReloadFileOpenInIse(string path)
        {
            var fileExists = File.Exists(path);
            this.IseIntegrator.GoToFile(path);
            if (this.IseIntegrator.IsFileSaved(path))
            {
                string question = fileExists ?
                    String.Format("File '{0}' has been modified by another program.\n\nDo you want to reload it?", path) :
                    String.Format("File '{0}' has been deleted or moved.\n\nDo you want to close it?", path);
                if (MessageBoxHelper.ShowQuestion("Reload file", question))
                {
                    this.IseIntegrator.CloseFile(path);
                    if (fileExists)
                    {
                        this.IseIntegrator.GoToFile(path);
                    }
                }
            }
            else
            {
                string message = fileExists ?
                    String.Format("File '{0}' has been modified by another program.\n\nSince the file has been changed in ISE editor, you will need to reload it manually.", path) :
                    String.Format("File '{0}' has been deleted or moved. Since the file has been changed in ISE editor, you will need to reload it manually.", path);
                MessageBoxHelper.ShowInfo(message);
            }
        }
    }
}