using System.Collections.Generic;

namespace Polymetal.Exchange.Utilities
{
    public class MagicItem
    {
        public List<double> Matrix;
        public List<MagicFace> Faces;
        public string Description;
        public List<string> Coverings;
        public MagicSolid Solid;
        public List<string> Properties;
        public MagicSystem SystemInfo;
    }

    public class MagicSystem
    {
        public string SystemDescription;
        public string SystemCode;
        public string SubSystemCode;
        public string SubSystemIndex;
        public string SystemType;
    }

    public class MagicFace
    {
        public List<double[]> Points;
    }

    public class MagicSolid
    {
        /// <summary>
        ///0 : Circle
        ///1: Rectangle
        ///2: Trapezium
        ///3: Ellipse
        ///4: RoundedRectangle
        ///5: Profile
        ///6: Circle
        ///7: ProfileType
        /// </summary>
        public byte Type;
        public double Height;
        public double[] Size;
        public double Thickness;
    }

    public static class MagicAttributes
    {
        public const string PdmsAbsorbDesc = "Pdms_Absorb_Desc";
        public const string PdmsAbsorbThickness = "Pdms_Absorb_Thickness";
        public const string PdmsAlignment = "Pdms_Alignment";
        public const string PdmsAngleDeg = "Pdms_Angle_deg";
        public const string PdmsAutoindex = "Pdms_Autoindex";
        public const string PdmsBlocked = "Pdms_Blocked";
        public const string PdmsBlockDp = "Pdms_Block_dp";
        public const string PdmsBoxHeight = "Pdms_Box_Height";
        public const string PdmsBoxLength = "Pdms_Box_Length";
        public const string PdmsBoxWidth = "Pdms_Box_Width";
        public const string PdmsCalcLs = "Pdms_Calc_ls";
        public const string PdmsComment = "Pdms_Comment";
        public const string PdmsComponent1 = "Pdms_Component_1";
        public const string PdmsComponent10 = "Pdms_Component_10";
        public const string PdmsComponent11 = "Pdms_Component_11";
        public const string PdmsComponent12 = "Pdms_Component_12";
        public const string PdmsComponent13 = "Pdms_Component_13";
        public const string PdmsComponent14 = "Pdms_Component_14";
        public const string PdmsComponent15 = "Pdms_Component_15";
        public const string PdmsComponent16 = "Pdms_Component_16";
        public const string PdmsComponent17 = "Pdms_Component_17";
        public const string PdmsComponent18 = "Pdms_Component_18";
        public const string PdmsComponent19 = "Pdms_Component_19";
        public const string PdmsComponent2 = "Pdms_Component_2";
        public const string PdmsComponent20 = "Pdms_Component_20";
        public const string PdmsComponent21 = "Pdms_Component_21";
        public const string PdmsComponent22 = "Pdms_Component_22";
        public const string PdmsComponent23 = "Pdms_Component_23";
        public const string PdmsComponent3 = "Pdms_Component_3";
        public const string PdmsComponent4 = "Pdms_Component_4";
        public const string PdmsComponent5 = "Pdms_Component_5";
        public const string PdmsComponent6 = "Pdms_Component_6";
        public const string PdmsComponent7 = "Pdms_Component_7";
        public const string PdmsComponent8 = "Pdms_Component_8";
        public const string PdmsComponent9 = "Pdms_Component_9";
        public const string PdmsComponentBottom = "Pdms_Component_Bottom";
        public const string PdmsComponentCentre = "Pdms_Component_Centre";
        public const string PdmsComponentTop = "Pdms_Component_Top";
        public const string PdmsComponentType = "Pdms_Component_Type";
        public const string PdmsConnectionHeight = "Pdms_Connection_Height";
        public const string PdmsConnectionSize1 = "Pdms_Connection_Size_1";
        public const string PdmsConnectionSize10 = "Pdms_Connection_Size_10";
        public const string PdmsConnectionSize11 = "Pdms_Connection_Size_11";
        public const string PdmsConnectionSize12 = "Pdms_Connection_Size_12";
        public const string PdmsConnectionSize13 = "Pdms_Connection_Size_13";
        public const string PdmsConnectionSize14 = "Pdms_Connection_Size_14";
        public const string PdmsConnectionSize2 = "Pdms_Connection_Size_2";
        public const string PdmsConnectionSize3 = "Pdms_Connection_Size_3";
        public const string PdmsConnectionSize4 = "Pdms_Connection_Size_4";
        public const string PdmsConnectionSize5 = "Pdms_Connection_Size_5";
        public const string PdmsConnectionSize6 = "Pdms_Connection_Size_6";
        public const string PdmsConnectionSize7 = "Pdms_Connection_Size_7";
        public const string PdmsConnectionSize8 = "Pdms_Connection_Size_8";
        public const string PdmsConnectionSize9 = "Pdms_Connection_Size_9";
        public const string PdmsDesc = "Pdms_Desc";
        public const string PdmsDiam = "Pdms_Diam";
        public const string PdmsDiamMm = "Pdms_Diam_mm";
        public const string PdmsDpSummKpa = "Pdms_dp_Summ_kpa";
        public const string PdmsDpSummPa = "Pdms_Dp_Summ_pa";
        public const string PdmsDpSummPam = "Pdms_dp_Summ_pam";
        public const string PdmsDuctDesc = "Pdms_Duct_Desc";
        public const string PdmsDuctType = "Pdms_Duct_Type";
        public const string PdmsFloor = "Pdms_Floor";
        public const string PdmsFloorFunc = "Pdms_Floor_Func";
        public const string PdmsHeightMm = "Pdms_Height_mm";
        public const string PdmsIfcGuid = "Pdms_Ifc_Guid";
        public const string PdmsInsulation1 = "Pdms_Insulation_1";
        public const string PdmsInsulation2 = "Pdms_Insulation_2";
        public const string PdmsInsulation3 = "Pdms_Insulation_3";
        public const string PdmsInsulationDesc = "Pdms_Insulation_Desc";
        public const string PdmsInsulationGost = "Pdms_Insulation_Gost";
        public const string PdmsInsulationMaterial = "Pdms_Insulation_Material";
        public const string PdmsInsulationThickness = "Pdms_Insulation_Thickness";
        public const string PdmsLengthMm = "Pdms_Length_mm";
        public const string PdmsLpARed = "Pdms_LpA_red";
        public const string PdmsMaterial = "Pdms_Material";
        public const string PdmsNationalCode = "Pdms_National_Code";
        public const string PdmsNozzLength = "Pdms_Nozz_Length";
        public const string PdmsPipeType = "Pdms_Pipe_Type";
        public const string PdmsPlant = "Pdms_Plant";
        public const string PdmsPStatPa = "Pdms_P_Stat_pa";
        public const string PdmsPSummKpa = "Pdms_p_Summ_kpa";
        public const string PdmsPSummPa = "Pdms_P_Summ_pa";
        public const string PdmsReverse = "Pdms_Reverse";
        public const string PdmsRoom = "Pdms_Room";
        public const string PdmsShape = "Pdms_Shape";
        public const string PdmsSize1 = "Pdms_Size_1";
        public const string PdmsSize2 = "Pdms_Size_2";
        public const string PdmsSize3 = "Pdms_Size_3";
        public const string PdmsSize4 = "Pdms_Size_4";
        public const string PdmsSize5 = "Pdms_Size_5";
        public const string PdmsSize6 = "Pdms_Size_6";
        public const string PdmsSize7 = "Pdms_Size_7";
        public const string PdmsSize8 = "Pdms_Size_8";
        public const string PdmsSize9 = "Pdms_Size_9";
        public const string PdmsSlope = "Pdms_Slope";
        public const string PdmsSpeed = "Pdms_Speed";
        public const string PdmsStatus1 = "Pdms_Status_1";
        public const string PdmsStatus2 = "Pdms_Status_2";
        public const string PdmsSystem1 = "Pdms_System_1";
        public const string PdmsSystem2 = "Pdms_System_2";
        public const string PdmsSystem3 = "Pdms_System_3";
        public const string PdmsSystem4 = "Pdms_System_4";
        public const string PdmsSystem5 = "Pdms_System_5";
        public const string PdmsSystem6 = "Pdms_System_6";
        public const string PdmsSystemUserCode = "Pdms_System_User_Code";
        public const string PdmsUserCode = "Pdms_User_Code";
        public const string PdmsValvePos = "Pdms_Valve_Pos";
        public const string PdmsVarComponent = "Pdms_Var_Component";
        public const string PdmsWidthMm = "Pdms_Width_mm";
        public const string PdmsSv1 = "Pdms_SV_1";
        public const string PdmsSv2 = "Pdms_SV_2";
        public const string PdmsSv3 = "Pdms_SV_3";
        public const string PdmsSv4 = "Pdms_SV_4";
        public const string PdmsSv5 = "Pdms_SV_5";
        public const string PdmsSv6 = "Pdms_SV_6";
    }
}
