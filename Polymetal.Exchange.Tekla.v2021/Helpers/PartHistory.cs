using System;
using System.Collections;
using Tekla.Structures.Model;

namespace Polymetal.Exchange.Tekla.v2021
{
    public class PartHistory
    {
        private static DateTime _createdDate;
        private static string _createdBy;
        private static DateTime _modifiedDate;
        private static string _modifiedBy;
        private static DateTime minDateTime = DateTime.Parse("01.01.1900 00:00");
        public DateTime CreatedDate
        {
            get { return _createdDate == DateTime.MinValue ? minDateTime : _createdDate; }
        }

        public string CreatedBy
        {
            get { return _createdBy; }
        }

        public DateTime ModifiedDate
        {
            get { return _modifiedDate == DateTime.MinValue ? minDateTime : _modifiedDate; }
        }

        public string ModifiedBy
        {
            get { return _modifiedBy; }
        }

        public PartHistory(Part part)
        {
            var properties = new ArrayList()
            {
                "OWNER",
                "HISTORY.CREATED",
                "HISTORY.CREATED_BY",
                "HISTORY.MODIFIED",
                "HISTORY.MODIFIED_BY"
            };
            var hashtable = new Hashtable();
            part.GetStringReportProperties(properties, ref hashtable);

            var strCreatedDate = (string)hashtable["HISTORY.CREATED"];
            var strModifiedDate = (string)hashtable["HISTORY.MODIFIED"];
            var strCreatedBy = (string)hashtable["HISTORY.CREATED_BY"];
            var strModifiedBy = (string)hashtable["HISTORY.MODIFIED_BY"];
            var owner = (string)hashtable["OWNER"];

            if (owner == null && strCreatedBy == null && strModifiedBy == null)
            {
                Standalone.SendMessage("Не удалось определить владельцы элемента " + part.Identifier.GUID);
                strCreatedBy = strModifiedBy = string.Empty;
            }
            else
            {
                strCreatedBy = strCreatedBy ?? (owner ?? strModifiedBy);
                strModifiedBy = strModifiedBy ?? strCreatedBy;
            }

            _createdBy = strCreatedBy.Replace("POLY/", "").Replace("POLY\\", string.Empty);
            _modifiedBy = strModifiedBy.Replace("POLY/", "").Replace("POLY\\", string.Empty);

            if (_createdBy.Contains("MicrosoftAccount/a.francych@outlook.com") || _createdBy.Contains("Anafran-VAIO/Anafran"))
                _createdBy = "KolendoAF";
            if (_modifiedBy.Contains("MicrosoftAccount/a.francych@outlook.com") || _modifiedBy.Contains("Anafran-VAIO/Anafran"))
                _modifiedBy = "KolendoAF";

            strCreatedDate = strCreatedDate ?? strModifiedDate;
            strModifiedDate = strModifiedDate ?? strCreatedDate;
            DateTime.TryParse(strCreatedDate, out _createdDate);
            DateTime.TryParse(strModifiedDate, out _modifiedDate);
            //_createdDate = _createdDate.Year <= 1900 ? RunTekla.TaskDateTime : _createdDate;
            //_modifiedDate = _modifiedDate.Year <= 1900 ? RunTekla.TaskDateTime : _modifiedDate;
        }
    }
}
