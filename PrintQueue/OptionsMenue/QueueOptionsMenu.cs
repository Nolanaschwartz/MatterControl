﻿/*
Copyright (c) 2014, Kevin Pope
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met: 

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer. 
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution. 

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies, 
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using System.IO;
using System.Diagnostics;
using System.Threading;

using MatterHackers.Agg.Image;
using MatterHackers.Agg.VertexSource;
using MatterHackers.Agg;
using MatterHackers.Agg.UI;
using MatterHackers.VectorMath;
using MatterHackers.MatterControl.DataStorage;
using MatterHackers.Localizations;

namespace MatterHackers.MatterControl.PrintQueue
{
    public class QueueOptionsMenu : GuiWidget
    {
        public DropDownMenu MenuDropList;
        private TupleList<string, Func<bool>> menuItems;

		ExportToFolderFeedbackWindow exportingWindow = null;

        public QueueOptionsMenu()
        {
			MenuDropList = new DropDownMenu(LocalizedString.Get("Queue Options"), Direction.Up);
            MenuDropList.HAnchor |= HAnchor.ParentLeft;
            MenuDropList.VAnchor |= VAnchor.ParentTop;
            SetMenuItems();

            AddChild(MenuDropList);
            this.Width = MenuDropList.Width;
            this.Height = MenuDropList.Height;
            this.Margin = new BorderDouble(4,0);
            this.Padding = new BorderDouble(0);
            this.MenuDropList.SelectionChanged += new EventHandler(MenuDropList_SelectionChanged);
        }

        void MenuDropList_SelectionChanged(object sender, EventArgs e)
        {
            string menuSelection = ((DropDownMenu)sender).SelectedValue;
            foreach (Tuple<string, Func<bool>> item in menuItems)
            {
                if (item.Item1 == menuSelection)
                {
                    if (item.Item2 != null)
                    {
                        item.Item2();
                    }
                }
            }
        }

        void SetMenuItems()
        {
            // The pdf export library is not working on the mac at the moment so we don't include the 
            // part sheet export option on mac.
            if (MatterHackers.Agg.UI.WindowsFormsAbstract.GetOSType() == WindowsFormsAbstract.OSType.Mac)
            {
                //Set the name and callback function of the menu items
                menuItems = new TupleList<string, Func<bool>> 
                {
                {"STL", null},
                {LocalizedString.Get(" Import from Zip"), importQueueFromZipMenu_Click},
                {LocalizedString.Get(" Export to Zip"), exportQueueToZipMenu_Click},
                {"GCode", null},
                {LocalizedString.Get(" Export to Folder"), exportGCodeToFolderButton_Click},
                };
            }
            else
            {
                //Set the name and callback function of the menu items
                menuItems = new TupleList<string, Func<bool>> 
                {
                {"STL", null},
                {LocalizedString.Get(" Import from Zip"), importQueueFromZipMenu_Click},
                {LocalizedString.Get(" Export to Zip"), exportQueueToZipMenu_Click},
                {"GCode", null},
                {LocalizedString.Get(" Export to Folder"), exportGCodeToFolderButton_Click},
                {LocalizedString.Get("Other"), null},
                {LocalizedString.Get(" Create Part Sheet"), createPartsSheetsButton_Click},
					{LocalizedString.Get(" Remove All"), removeAllFromQueueButton_Click},
                };
            }

            BorderDouble padding = MenuDropList.MenuItemsPadding;
            //Add the menu items to the menu itself
            foreach (Tuple<string, Func<bool>> item in menuItems)
            {
                if (item.Item2 == null)
                {
                    MenuDropList.MenuItemsPadding = new BorderDouble(5, 0, padding.Right, 3);
                }
                else
                {
                    MenuDropList.MenuItemsPadding = new BorderDouble(10, 5, padding.Right, 5);
                }

                MenuDropList.AddItem(item.Item1);
            }

            MenuDropList.Padding = padding;
        }

        bool createPartsSheetsButton_Click()
        {
            UiThread.RunOnIdle(PartSheetClickOnIdle);
            return true;
        }

        void PartSheetClickOnIdle(object state)
        {
            List<PrintItem> parts = QueueData.Instance.CreateReadOnlyPartList();
            if (parts.Count > 0)
            {
                SaveFileDialogParams saveParams = new SaveFileDialogParams("Save Parts Sheet|*.pdf");

				saveParams.ActionButtonLabel = LocalizedString.Get("Save Parts Sheet");
				string saveParamsTitleLabel = "MatterControl".Localize();
				string saveParamsTitleLabelFull = LocalizedString.Get ("Save");
				saveParams.Title = string.Format("{0}: {1}",saveParamsTitleLabel,saveParamsTitleLabelFull);

                System.IO.Stream streamToSaveTo = FileDialog.SaveFileDialog(ref saveParams);
				if (streamToSaveTo != null) 
				{
					streamToSaveTo.Close ();
				}

				if (saveParams.FileName != null)
                {
                    PartsSheet currentPartsInQueue = new PartsSheet(parts, saveParams.FileName);

                    currentPartsInQueue.SaveSheets();

                    SavePartsSheetFeedbackWindow feedbackWindow = new SavePartsSheetFeedbackWindow(parts.Count, parts[0].Name, ActiveTheme.Instance.PrimaryBackgroundColor);
                    currentPartsInQueue.UpdateRemainingItems += feedbackWindow.StartingNextPart;
                    currentPartsInQueue.DoneSaving += feedbackWindow.DoneSaving;

                    feedbackWindow.ShowAsSystemWindow();
                }
            }
        }

        void MustSelectPrinterMessage(object state)
        {
            StyledMessageBox.ShowMessageBox("You must select a printer before you can export printable files.", "You must select a printer");
        }

        bool exportGCodeToFolderButton_Click()
        {
            if (ActivePrinterProfile.Instance.ActivePrinter == null)
            {
                UiThread.RunOnIdle(MustSelectPrinterMessage);
            }
            else
            {
                UiThread.RunOnIdle(SelectLocationToExportGCode);
            }

            return true;
        }

		void ExportToFolderFeedbackWindow_Closed(object sender, EventArgs e)
		{
			this.exportingWindow = null;
		}

		private void SelectLocationToExportGCode(object state)
        {
            SelectFolderDialogParams selectParams = new SelectFolderDialogParams("Select Location To Save Files");
			selectParams.ActionButtonLabel = LocalizedString.Get("Export");
            selectParams.Title = "MatterControl: Select A Folder";

            string path = FileDialog.SelectFolderDialog(ref selectParams);
            if (path != null && path != "")
            {
                List<PrintItem> parts = QueueData.Instance.CreateReadOnlyPartList();
                if (parts.Count > 0)
                {
                    if (exportingWindow == null)
                    {
                        exportingWindow = new ExportToFolderFeedbackWindow(parts.Count, parts[0].Name, ActiveTheme.Instance.PrimaryBackgroundColor);
                        exportingWindow.Closed += new EventHandler(ExportToFolderFeedbackWindow_Closed);
                        exportingWindow.ShowAsSystemWindow();
                    }
                    else
                    {
                        exportingWindow.BringToFront();
                    }

                    ExportToFolderProcess exportToFolderProcess = new ExportToFolderProcess(parts, path);
                    exportToFolderProcess.StartingNextPart += exportingWindow.StartingNextPart;
                    exportToFolderProcess.UpdatePartStatus += exportingWindow.UpdatePartStatus;
                    exportToFolderProcess.DoneSaving += exportingWindow.DoneSaving;
                    exportToFolderProcess.Start();
                }
            }
        }

        bool exportQueueToZipMenu_Click()
        {
            UiThread.RunOnIdle(ExportQueueToZipOnIdle);
            return true;
        }

        void ExportQueueToZipOnIdle(object state)
        {
            List<PrintItem> partList = QueueData.Instance.CreateReadOnlyPartList();
            ProjectFileHandler project = new ProjectFileHandler(partList);
            project.SaveAs();
        }

        bool importQueueFromZipMenu_Click()
        {
            UiThread.RunOnIdle(ImportQueueFromZipMenuOnIdle);
            return true;
        }
        
        void ImportQueueFromZipMenuOnIdle(object state)
        {
            ProjectFileHandler project = new ProjectFileHandler(null);
            List<PrintItem> partFiles = project.OpenFromDialog();
            if (partFiles != null)
            {
                QueueData.Instance.RemoveAll();
                foreach (PrintItem part in partFiles)
                {
                    QueueData.Instance.AddItem(new PrintItemWrapper(new PrintItem(part.Name, part.FileLocation)));
                }
            }
        }

		bool removeAllFromQueueButton_Click()
		{
			UiThread.RunOnIdle (removeAllPrintsFromQueue);
			return true;
		}

		void removeAllPrintsFromQueue (object state)
		{
			QueueData.Instance.RemoveAll();
		}
    }
}
