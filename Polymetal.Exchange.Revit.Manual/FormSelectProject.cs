using Polymetal.Exchange.Utilities;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Windows.Forms;

namespace Polymetal.Exchange.Revit.Manual
{
    public partial class FormSelectProject : Form
    {
        private string _filePath;
        private decimal _selectedProjectId;
        internal decimal SelectedProjectId => _selectedProjectId;

        private string _selectedUser;
        internal string SelectedUser => _selectedUser;

        public FormSelectProject(string filePath)
        {
            InitializeComponent();
            _filePath = filePath;
        }

        private Dictionary<decimal, string> GetPorjects()
        {
            var result = new Dictionary<decimal, string>();
            var cmd = "select ProjectID, ModelPath=REPLACE(ModelPath,'\\\\spbfs02\\cad\\Revit','..') from ExchangeProject where Program = 'revit' and NeedExchange = 1 and Version = '2024' order by ModelPath";
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();

                using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var projectId = (decimal)reader["ProjectID"];
                        var modelPath = (string)reader["ModelPath"];
                        result.Add(projectId, modelPath);
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }

            return result;
        }

        private Dictionary<string, string> GetUsers()
        {
            var result = new Dictionary<string, string>();
            var cmd = "select UserLogin, UserName from [user] where UserPosition <>'dead' order by UserName";
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();

                using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var userLogin = (string)reader["UserLogin"];
                        var userName = (string)reader["UserName"];
                        result.Add(userLogin, userName);
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }

            return result;
        }

        private void FormSelectProject_Shown(object sender, System.EventArgs e)
        {
            var projectsDictionary = GetPorjects();
            var usersDictionary = GetUsers();

            comboBoxProject.DataSource = new BindingSource(projectsDictionary, null);
            comboBoxProject.ValueMember = "KEY";
            comboBoxProject.DisplayMember = "VALUE";

            comboBoxUser.DataSource = new BindingSource(usersDictionary, null);
            comboBoxUser.ValueMember = "KEY";
            comboBoxUser.DisplayMember = "VALUE";
        }

        private void buttonSelect_Click(object sender, System.EventArgs e)
        {
            if (comboBoxProject.SelectedValue != null && comboBoxUser.SelectedValue != null)
            {
                _selectedProjectId = (decimal) comboBoxProject.SelectedValue;
                _selectedUser = (string) comboBoxUser.SelectedValue;
            }
        }
    }
}
