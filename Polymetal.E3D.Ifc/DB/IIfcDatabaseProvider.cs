using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Polymetal.E3D.Ifc.DB
{
    interface IIFcDatabaseProvider
    {
        /// <summary>
        /// Получить идентификатор (IdTask) для ревизии задания.
        /// </summary>
        /// <param name="taskId">Идентификатор задания</param>
        /// <param name="revisionNumber">Номер ревизии</param>
        /// <returns>Строковое значение IdTask для ревизии</returns>
        string GetRevisionIdByRevisionNum(int taskId, int revisionNumber);
    }
}
