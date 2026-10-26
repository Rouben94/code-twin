using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.CustomMarshalers;

namespace CodeTwin.Vsix;

// The few TwinCAT Automation Interface (TCatSysManagerLib) members CodeTwin uses, declared here so the
// build needs no Beckhoff assembly. IIDs and vtable order match the type library; _VtblGap skips unused slots.

[ComImport]
[Guid("25FC8633-1B78-4F0D-A4F8-A96DACCC110E")]
internal interface ITcSysManager
{
    void _VtblGap1_4();

    [return: MarshalAs(UnmanagedType.Interface)]
    ITcSmTreeItem LookupTreeItem([In, MarshalAs(UnmanagedType.BStr)] string bstrItem);
}

[ComImport]
[Guid("A935C4B0-668C-42C9-9815-0441FCB623C6")]
internal interface ITcSmTreeItem : IEnumerable
{
    [return: MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(EnumeratorToEnumVariantMarshaler))]
    new IEnumerator GetEnumerator();

    string Name
    {
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [param: In, MarshalAs(UnmanagedType.BStr)]
        set;
    }

    int ItemType { get; }

    void _VtblGap2_2();

    string ItemSubTypeName
    {
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
    }

    void _VtblGap3_10();

    [return: MarshalAs(UnmanagedType.Interface)]
    ITcSmTreeItem CreateChild(
        [In, MarshalAs(UnmanagedType.BStr)] string bstrName,
        [In] int nSubType,
        [In, MarshalAs(UnmanagedType.BStr)] string bstrBefore,
        [In, MarshalAs(UnmanagedType.Struct)] object? vInfo);
}

[ComImport]
[Guid("E937E819-2185-420D-91C0-3D8136FCC517")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITcPlcDeclaration
{
    string? DeclarationText
    {
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [param: In, MarshalAs(UnmanagedType.BStr)]
        set;
    }
}

[ComImport]
[Guid("1E70BCEE-A064-4A22-A408-9EA73954AF16")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITcPlcImplementation
{
    string? ImplementationText
    {
        [return: MarshalAs(UnmanagedType.BStr)]
        get;
        [param: In, MarshalAs(UnmanagedType.BStr)]
        set;
    }
}

/// <summary>The PLC values of TREEITEMTYPES, without the TREEITEMTYPE_ prefix.</summary>
internal enum PlcItemType
{
    PROJECTDEF = 56,
    TCOMPLCOBJECT = 57,
    PLCAPP = 600,
    PLCFOLDER = 601,
    PLCPOUPROG = 602,
    PLCPOUFUNC = 603,
    PLCPOUFB = 604,
    PLCDUTENUM = 605,
    PLCDUTSTRUCT = 606,
    PLCDUTUNION = 607,
    PLCACTION = 608,
    PLCMETHOD = 609,
    PLCITFMETH = 610,
    PLCPROP = 611,
    PLCITFPROP = 612,
    PLCPROPGET = 613,
    PLCPROPSET = 614,
    PLCGVL = 615,
    PLCTRANSITION = 616,
    PLCLIBMAN = 617,
    PLCITF = 618,
    PLCVISOBJ = 619,
    PLCVISMAN = 620,
    PLCTASK = 621,
    PLCPROJECTINFO = 622,
    PLCDUTALIAS = 623,
    PLCTARGETVISU = 624,
    PLCGLOBALTEXTLIST = 625,
    PLCTEXTLIST = 626,
    PLCGLOBALIMAGEPOOL = 627,
    PLCIMAGEPOOL = 628,
    PLCGVLPARAMLIST = 629,
    PLCWEBVISU = 630,
    PLCUMLCLASSDIAGRAM = 631,
    PLCRECIPEMANAGER = 632,
    PLCRECIPEDEFINITION = 633,
    PLCTRENDOBJECT = 634,
    PLCTRENDMANAGER = 635,
    PLCPROGREF = 650,
    PLCEXTDATATYPE = 651,
    PLCEXTDATATYPECONT = 652,
    PLCTMCDESCRIPTION = 653,
    PLCITFPROPGET = 654,
    PLCITFPROPSET = 655,
    PLCLIBREF = 656,
    PLCLIBPLACEHOLDER = 657,
    PLCTEXTLISTENUMERATION = 658,
}
