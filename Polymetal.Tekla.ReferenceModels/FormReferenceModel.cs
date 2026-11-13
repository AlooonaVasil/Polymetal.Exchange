using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TS = Tekla.Structures;
using TSM = Tekla.Structures.Model;

namespace Polymetal.Tekla.ReferenceModels
{
    public partial class Form_ReferenceModel : Form
    {
        private static string referenceModelPath;
        private static TSM.Model _model = null;
        private static DataTable stpDt;
        private static string modelPath;
        private static BindingSource binding = new BindingSource();
        private static string cachePath = string.Empty;

        private enum ModelState
        {
            New,
            Modified,
            Deleted,
            Old
        }

        public Form_ReferenceModel(TSM.Model model, string path)
        {
            InitializeComponent();
            InitializeThisForm(model, path);
        }

        private void InitializeThisForm(TSM.Model model, string path)
        {
            _model = model;
            referenceModelPath = path;
            modelPath = _model.GetInfo().ModelPath;
            TS.TeklaStructuresSettings.GetAdvancedOption("XS_REFERENCE_CACHE", ref cachePath);
            treeView1.Nodes.Clear();
            stpDt = new DataTable();
            stpDt.Columns.Add("File");
            stpDt.Columns.Add("Date", typeof(DateTime));
            stpDt.Columns.Add("State", typeof(ModelState));
            stpDt.Columns.Add("Visible", typeof(TSM.ReferenceModel.VisibilityEnum));
            stpDt.Columns.Add("RefModel", typeof(TSM.ReferenceModel));
            stpDt.Columns.Add("ModelName");
            stpDt.DefaultView.Sort = "File ASC";
            stpDt.PrimaryKey = new[] { stpDt.Columns["File"] };
            progressBar1.Hide();
        }

        private void Form_ReferenceModel_Load(object sender, EventArgs e)
        {
            UpdateStpFilesDataTable(true);
            fileSystemWatcher1.Path = referenceModelPath;
            fileSystemWatcher1.NotifyFilter = NotifyFilters.LastWrite |
                NotifyFilters.Attributes |
                NotifyFilters.CreationTime |
                NotifyFilters.DirectoryName |
                NotifyFilters.FileName |
                NotifyFilters.LastAccess |
                NotifyFilters.Size;
            fileSystemWatcher1.Filter = "*.stp";
            fileSystemWatcher1.Changed += OnChanged;
            fileSystemWatcher1.Created += OnChanged;
            fileSystemWatcher1.Deleted += OnChanged;
            fileSystemWatcher1.EnableRaisingEvents = true;

            binding.DataSource = stpDt;
            comboBox_Search.DataSource = binding;
            comboBox_Search.DisplayMember = "ModelName";
            comboBox_Search.ValueMember = "ModelName";
        }

        private void UpdateStpFilesDataTable(bool firstLoad = false)
        {
            var stpFilesList = ConvertFileNames(Directory.GetFiles(referenceModelPath, "*.stp", SearchOption.AllDirectories).ToList());
            var rmFilesList = new List<string>();
            var rmList = new List<TSM.ReferenceModel>();
            var objects = _model.GetModelObjectSelector().GetAllObjectsWithType(TSM.ModelObject.ModelObjectEnum.REFERENCE_MODEL);
            while (objects.MoveNext())
            {
                if (!(objects.Current is TSM.ReferenceModel)) continue;
                var rm = objects.Current as TSM.ReferenceModel;
                if (rm == null || !rm.Filename.Contains("RefModels")) continue;
                rmFilesList.Add(rm.Filename);
                rmList.Add(rm);
            }
            rmFilesList = ConvertFileNames(rmFilesList);

            var except = stpFilesList.Except(rmFilesList).ToList();
            except.AddRange(rmFilesList.Except(stpFilesList).ToList());
            var intersect = stpFilesList.Intersect(rmFilesList);

            foreach (var ex in except)
            {
                if (stpFilesList.Contains(ex))
                {
                    //new
                    var fRows = stpDt.DefaultView.FindRows(ex);
                    if (fRows.Length == 0)
                    {
                        var row = stpDt.NewRow();
                        row["File"] = ex;
                        row["Date"] = new FileInfo(Path.Combine(referenceModelPath, ex)).LastWriteTime;
                        row["State"] = ModelState.New;
                        row["Visible"] = TSM.ReferenceModel.VisibilityEnum.HIDDEN;
                        row["ModelName"] = ex.Split(Path.DirectorySeparatorChar).Last();
                        stpDt.Rows.Add(row);
                    }
                    else
                    {
                        var row = fRows[0].Row;
                        row["Date"] = new FileInfo(Path.Combine(referenceModelPath, ex)).LastWriteTime;
                        row["State"] = ModelState.New;
                        row["Visible"] = TSM.ReferenceModel.VisibilityEnum.HIDDEN;
                    }
                }
                else
                {
                    //del
                    var fRows = stpDt.DefaultView.FindRows(ex);
                    if (fRows.Length == 0)
                    {
                        var row = stpDt.NewRow();
                        row["File"] = ex;
                        var fInfo = new FileInfo(Path.Combine(referenceModelPath, ex));
                        if (fInfo.Exists)
                        {
                            row["Date"] = fInfo.LastWriteTime;
                        }
                        else
                        {
                            row["Date"] = DateTime.MinValue;
                        }
                        row["State"] = ModelState.Deleted;
                        var rm = rmList[rmFilesList.IndexOf(ex)];
                        row["Visible"] = rm.Visibility;
                        row["RefModel"] = rm;
                        row["ModelName"] = ex.Split(Path.DirectorySeparatorChar).Last();
                        stpDt.Rows.Add(row);
                    }
                    else
                    {
                        var row = fRows[0].Row;
                        row["State"] = ModelState.Deleted;
                        var rm = rmList[rmFilesList.IndexOf(ex)];
                        row["Visible"] = rm.Visibility;
                        row["RefModel"] = rm;
                    }
                }
            }

            foreach (var inter in intersect)
            {
                //compare date
                var fRows = stpDt.DefaultView.FindRows(inter);
                if (fRows.Length == 0)
                {
                    var row = stpDt.NewRow();
                    row["File"] = inter;
                    var fInfo = new FileInfo(Path.Combine(referenceModelPath, inter));
                    if (fInfo.Exists)
                    {
                        row["Date"] = fInfo.LastWriteTime;
                        row["State"] = ModelState.Old;
                    }
                    else
                    {
                        row["Date"] = DateTime.MinValue;
                        row["State"] = ModelState.Deleted;
                    }
                    var rm = rmList[rmFilesList.IndexOf(inter)];
                    row["Visible"] = rm.Visibility;
                    row["RefModel"] = rm;
                    row["ModelName"] = inter.Split(Path.DirectorySeparatorChar).Last();
                    stpDt.Rows.Add(row);
                }
                else
                {
                    var row = fRows[0].Row;
                    var exDateTime = (DateTime)row["Date"];
                    var fInfo = new FileInfo(Path.Combine(referenceModelPath, inter));
                    if (fInfo.Exists)
                    {
                        var fDate = fInfo.LastWriteTime;
                        if (fDate > exDateTime)
                        {
                            row["Date"] = fDate;
                            row["State"] = ModelState.Modified;
                        }
                        else
                        {
                            row["Date"] = fDate;
                            row["State"] = ModelState.Old;
                        }
                    }
                    else
                    {
                        row["Date"] = DateTime.MinValue;
                        row["State"] = ModelState.Deleted;
                    }

                    var rm = rmList[rmFilesList.IndexOf(inter)];
                    row["Visible"] = rm.Visibility;
                    row["RefModel"] = rm;
                }
            }

            if (Directory.Exists(cachePath))
            {
                var rows = stpDt.Select("State = " + (int)ModelState.Old);
                foreach (var row in rows)
                {
                    var name = row["ModelName"].ToString().Replace(".stp", string.Empty);
                    var cacheFiles = Directory.GetFiles(cachePath, "*" + name + ".tsfo", SearchOption.TopDirectoryOnly);

                    if (cacheFiles.Length > 0)
                    {
                        var lastCacheDate = DateTime.MinValue;
                        foreach (var f in cacheFiles)
                        {
                            var date = new FileInfo(f).LastWriteTime;
                            if (date > lastCacheDate)
                            {
                                lastCacheDate = date;
                            }
                        }

                        if ((DateTime)row["Date"] > lastCacheDate)
                        {
                            row["State"] = ModelState.Modified;
                        }
                    }
                }
            }

            UpdateTreeView(firstLoad);
        }

        private List<string> ConvertFileNames(List<string> input)
        {
            var output = new List<string>();
            for (int i = 0; i < input.Count; i++)
            {
                var fInfo = new FileInfo(input[i]);
                output.Add(fInfo.FullName.Replace(referenceModelPath + "\\", string.Empty));
            }
            return output;
        }

        /*private TreeNode SearchTreeNode(string path)
        {
            TreeNode result;
            var nodes = treeView1.Nodes.Find(path, true);
            if (nodes.Length == 0)
            {
                var splitPath = path.Split(Path.DirectorySeparatorChar);
                var curPath = splitPath[0];
                var siteNodes = treeView1.Nodes.Find(curPath, false);
                TreeNode siteNode;
                if (siteNodes.Length == 0)
                {
                    siteNode = treeView1.Nodes.Add(curPath, curPath);
                    siteNode.ImageIndex = 4;
                    siteNode.SelectedImageIndex = 4;
                }
                else
                {
                    siteNode = siteNodes.First();
                }

                curPath = splitPath[1];
                var zoneNodes = siteNode.Nodes.Find(Path.Combine(siteNode.FullPath, curPath), false);
                TreeNode zoneNode;
                if (zoneNodes.Length == 0)
                {
                    zoneNode = siteNode.Nodes.Add(Path.Combine(siteNode.FullPath, curPath), curPath);
                    zoneNode.ImageIndex = 5;
                    zoneNode.SelectedImageIndex = 5;
                }
                else
                {
                    zoneNode = zoneNodes.First();
                }

                curPath = splitPath[2];
                result = zoneNode.Nodes.Add(Path.Combine(zoneNode.FullPath, curPath), curPath);
            }
            else
            {
                result = nodes.First();
            }
            return result;
        }*/

        private TreeNode SearchTreeNode(string path)
        {
            var nodes = treeView1.Nodes.Find(path, true);
            return nodes.Length == 0 ? treeView1.Nodes.Add(path,path.Replace(".stp",string.Empty)) : nodes.First();
        }

        private void UpdateTreeView(bool firstLoad = false)
        {            
            /*var visibleList = new List<TreeNode>();
            if (!firstLoad)
            {
                visibleList.AddRange(treeView1.Nodes.Cast<TreeNode>().Where(node => node.IsVisible));
            }*/

            foreach (DataRow row in stpDt.Rows)
            {
                var name = row["File"].ToString();
                var state = (ModelState)row["State"];
                var visible = (TSM.ReferenceModel.VisibilityEnum)row["visible"];
                var node = SearchTreeNode(name);
                SetNodeStyle(node, visible, state);
            }

            treeView1.Sort();
            /*if (!firstLoad)
            {
                var removingNodes = new List<TreeNode>();
                foreach (TreeNode node in treeView1.Nodes)
                {
                    removingNodes.AddRange(node.Nodes.Cast<TreeNode>().Where(node2 => node2.Nodes.Count == 0));

                    if (removingNodes.Count == node.Nodes.Count)
                    {
                        removingNodes.Add(node);
                    }
                }

                foreach (var node in removingNodes.Where(node => node != null))
                {
                    node.Remove();
                }

                foreach (var node in visibleList)
                {
                    node.EnsureVisible();
                }
            }*/
            foreach (TreeNode node in treeView1.Nodes)
            {
                node.EnsureVisible();
            }
            var rows = stpDt.Select("State <> " + (int)ModelState.Old);
            if (rows.Length > 0)
            {
                Text = "Список опорных моделей (опорная модель не актуальна, обновите модель)";
                button_Update.Enabled = true;
            }
            else
            {
                Text = "Список опорных моделей";
                button_Update.Enabled = false;
            }
        }

        private void SetNodeStyle(TreeNode node, TSM.ReferenceModel.VisibilityEnum visible, ModelState state)
        {
            var imageIndex = -1;
            var font = treeView1.Font;
            var color = treeView1.ForeColor;

            imageIndex = visible == TSM.ReferenceModel.VisibilityEnum.VISIBLE ? 0 : 3;

            switch (state)
            {
                case ModelState.New:
                    color = Color.Green;
                    font = new Font(treeView1.Font, FontStyle.Bold);
                    break;
                case ModelState.Modified:
                    color = Color.Blue;
                    font = new Font(treeView1.Font, FontStyle.Bold);
                    break;
                case ModelState.Deleted:
                    font = new Font(treeView1.Font, FontStyle.Strikeout);
                    color = Color.Red;
                    break;
            }

            node.ImageIndex = imageIndex;
            node.SelectedImageIndex = imageIndex;
            node.NodeFont = font;
            node.ForeColor = color;
        }

        private bool ReloadForm()
        {
            if (_model.GetInfo().ModelPath.Equals(modelPath))
            {
                return false;
            }
            var model = new TSM.Model();
            var referencePath = Path.Combine(model.GetInfo().ModelPath, "RefModels");
            InitializeThisForm(model, referencePath);
            Form_ReferenceModel_Load(null, null);
            return true;
        }

        private void button_Update_Click(object sender, EventArgs e)
        {
            if (ReloadForm())
            {
                return;
            }
            var rowsToDelete = new List<DataRow>();
            progressBar1.Value = 0;
            progressBar1.Minimum = 0;
            progressBar1.Maximum = stpDt.Rows.Count-1;
            progressBar1.Show();

            for (var i = 0; i < stpDt.Rows.Count; i++)
            {
                progressBar1.Value = i;
                Invalidate();
                var row = stpDt.Rows[i];
                var state = (ModelState)row["State"];
                switch (state)
                {
                    case ModelState.New:
                    {
                        var rm = new TSM.ReferenceModel();
                        rm.Filename = Path.Combine(referenceModelPath, row["File"].ToString());
                        rm.Position.Zero();
                        rm.Scale = 1.0;
                        rm.Visibility = TSM.ReferenceModel.VisibilityEnum.HIDDEN;
                        rm.Insert();
                        rm.SetUserProperty("Group", "Aveva PDMS Models");
                        row["RefModel"] = rm;
                        row["State"] = ModelState.Old;
                    }
                        break;
                    case ModelState.Deleted:
                    {
                        var rm = row["RefModel"] is DBNull ? null : (TSM.ReferenceModel)row["RefModel"];
                        if (rm == null) continue;
                        rm.Visibility = TSM.ReferenceModel.VisibilityEnum.HIDDEN;
                        rm.Delete();
                        rowsToDelete.Add(row);
                    }
                        break;
                    case ModelState.Modified:
                    {
                        var rm = row["RefModel"] is DBNull ? null : (TSM.ReferenceModel)row["RefModel"];
                        if (rm == null) continue;
                        row["Visible"] = rm.Visibility;
                        if (rm.Visibility == TSM.ReferenceModel.VisibilityEnum.VISIBLE)
                        {
                            rm.Visibility = TSM.ReferenceModel.VisibilityEnum.HIDDEN;
                            rm.Visibility = TSM.ReferenceModel.VisibilityEnum.VISIBLE;
                            rm.Modify();
                        }
                        row["State"] = ModelState.Old;
                    }
                        break;
                    case ModelState.Old:
                    {
                        var rm = row["RefModel"] is DBNull ? null : (TSM.ReferenceModel)row["RefModel"];
                        if (rm == null) continue;
                        row["Visible"] = rm.Visibility;
                    }
                        break;
                }                
            }

            foreach (var row in rowsToDelete)
            {
                var name = row["File"].ToString();
                var node = SearchTreeNode(name);
                node.Remove();
                stpDt.Rows.Remove(row);
            }

            progressBar1.Hide();
            _model.CommitChanges();
            UpdateTreeView();
        }

        private void button_Show_Click(object sender, EventArgs e)
        {
            ShowModel(TSM.ReferenceModel.VisibilityEnum.VISIBLE);
            treeView1_AfterSelect(null, null);
        }

        private void button_Hide_Click(object sender, EventArgs e)
        {
            ShowModel(TSM.ReferenceModel.VisibilityEnum.HIDDEN);
        }

        private void ShowModel(TSM.ReferenceModel.VisibilityEnum visibility)
        {
            var nodes = treeView1.SelectedNodes.ToList();
            var count = nodes.Count;
            if (count == 0) return;

            progressBar1.Value = 0;
            progressBar1.Minimum = 0;
            progressBar1.Maximum = count-1;
            progressBar1.Show();

            for (var i = 0; i < nodes.Count; i++)
            {
                progressBar1.Value = i;
                Invalidate();
                var node = nodes[i];
                var rows = stpDt.Select("File LIKE '*" + node.Name + "'");
                if (rows.Length == 0) continue;
                var row = rows.First();
                var rm = row["RefModel"] is DBNull ? null : (TSM.ReferenceModel)row["RefModel"];
                if (rm == null || !File.Exists(rm.Filename) || rm.Visibility == visibility) continue;

                rm.Visibility = visibility;
                rm.Modify();
                _model.CommitChanges();
                row["Visible"] = visibility;
                node.ImageIndex = visibility == TSM.ReferenceModel.VisibilityEnum.VISIBLE ? 0 : 3;
                node.SelectedImageIndex = visibility == TSM.ReferenceModel.VisibilityEnum.VISIBLE ? 0 : 3;                
            }
            progressBar1.Hide();
            _model.CommitChanges();
            UpdateTreeView();
        }

        private List<TreeNode> GetLowestNodes(TreeNode rootNode)
        {
            var result = new List<TreeNode>();
            if (rootNode == null)
            {
                return result;
            }
            if (rootNode.Nodes.Count == 0)
            {
                result.Add(rootNode);
            }
            else
            {
                foreach (TreeNode node in rootNode.Nodes)
                {
                    result.AddRange(GetLowestNodes(node));
                }
            }
            return result;
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            var ObjectsToSelect = new ArrayList();
            var selected = treeView1.SelectedNodes;
            foreach (var node in selected)
            {
                if (node.Nodes.Count != 0) continue;
                var rows = stpDt.Select("File LIKE '*" + node.Name+ "'");
                if (rows.Length == 0) continue;
                var row = rows.First();
                var rm = row["RefModel"] is DBNull ? null : (TSM.ReferenceModel)row["RefModel"];
                if (rm == null || !File.Exists(rm.Filename) || rm.Visibility == TSM.ReferenceModel.VisibilityEnum.HIDDEN) continue;

                ObjectsToSelect.Add(rm);
            }

            if (ObjectsToSelect.Count <= 0) return;
            var MS = new TSM.UI.ModelObjectSelector();
            MS.Select(ObjectsToSelect);
        }

        private void Form_ReferenceModel_FormClosing(object sender, FormClosingEventArgs e)
        {

        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            var path = e.FullPath.Replace(referenceModelPath + "\\", string.Empty);
            var rows = stpDt.Select("File = '" + path + "'");
            if (rows.Length > 0)
            {
                foreach (DataRow row in rows)
                {
                    if (e.ChangeType == WatcherChangeTypes.Changed && (ModelState)row["State"] != ModelState.New)
                    {
                        row["State"] = ModelState.Modified;
                    }
                    else if (e.ChangeType == WatcherChangeTypes.Created && (ModelState)row["State"] != ModelState.New)
                    {
                        row["State"] = ModelState.Modified;
                    }
                    else if (e.ChangeType == WatcherChangeTypes.Deleted && !File.Exists(e.FullPath))
                    {
                        row["State"] = ModelState.Deleted;
                    }
                }
            }
            else
            {
                var row = stpDt.NewRow();
                row["File"] = path;
                row["Date"] = new FileInfo(Path.Combine(referenceModelPath, path)).LastWriteTime;
                row["State"] = ModelState.New;
                row["Visible"] = TSM.ReferenceModel.VisibilityEnum.HIDDEN;
                row["ModelName"] = path.Split(Path.DirectorySeparatorChar).Last();
                stpDt.Rows.Add(row);
            }
            UpdateTreeView();
        }

        private void comboBox_Search_TextUpdate(object sender, EventArgs e)
        {
            string text = comboBox_Search.Text;
            binding.Filter = "File LIKE '*" + text + "*'"; //это действие изменяет свойство Text, т. е. затирает то что было введено юзером
            comboBox_Search.Text = text; //тут восстанавливаем последствия предыдущей строки
            comboBox_Search.SelectionStart = text.Length;
            comboBox_Search.DroppedDown = true;
        }

        private void comboBox_Search_SelectionChangeCommitted(object sender, EventArgs e)
        {
            var selectedRow = (DataRowView)comboBox_Search.SelectedItem;
            if (selectedRow != null)
            {
                var name = selectedRow["File"].ToString();
                var node = SearchTreeNode(name);
                if (node != null)
                {
                    treeView1.SelectedNodes = new List<TreeNode>() { node };
                    node.EnsureVisible();
                }
            }
            binding.Filter = null;
        }
    }
}
