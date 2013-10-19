﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using BrawlLib.SSBBTypes;
using Ikarus;

namespace BrawlLib.SSBB.ResourceNodes
{
    public unsafe class MoveDefModelVisibilityNode : MoveDefEntry
    {
        internal FDefModelDisplay* Header { get { return (FDefModelDisplay*)WorkingUncompressed.Address; } }

        FDefModelDisplay hdr;

        [Category("Model Visibility")]
        public int EntryOffset { get { return hdr._entryOffset; } }
        [Category("Model Visibility")]
        public int EntryCount { get { return hdr._entryCount; } }
        [Category("Model Visibility")]
        public int DefaultsOffset { get { return hdr._defaultsOffset; } }
        [Category("Model Visibility")]
        public int DefaultsCount { get { return hdr._defaultsCount; } }

        public override bool OnInitialize()
        {
            base.OnInitialize();
            _name = "Model Visibility";
            hdr = *Header;
            return true;
        }

        public override void OnPopulate()
        {
            VoidPtr entries = BaseAddress + EntryOffset;
            VoidPtr defaults = BaseAddress + DefaultsOffset;

            for (int i = 0; i < (EntryOffset == 0 ? 0 : ((DefaultsOffset == 0 ? _offset : DefaultsOffset) - EntryOffset) / 4); i++)
            {
                MoveDefModelVisRefNode offset;
                (offset = new MoveDefModelVisRefNode() { _name = "Reference" + (i + 1) }).Initialize(this, entries + i * 4, 4);
                
                if (offset.DataOffset == 0)
                    continue;

                if (_root.GetSize(offset.DataOffset) != EntryCount * 8)
                    Console.WriteLine(_root.GetSize(offset.DataOffset) - EntryCount * 8);

                VoidPtr offAddr = BaseAddress + offset.DataOffset;
                for (int c = 0; c < EntryCount; c++)
                {
                    MoveDefBoneSwitchNode Switch;
                    (Switch = new MoveDefBoneSwitchNode() { _name = "BoneSwitch" + c }).Initialize(offset, offAddr + c * 8, 8);
                    int sCount = Switch.Count;
                    VoidPtr gAddr = BaseAddress + Switch.DataOffset;
                    for (int s = 0; s < sCount; s++)
                    {
                        MoveDefModelVisGroupNode Group;
                        (Group = new MoveDefModelVisGroupNode() { _name = "BoneGroup" + s }).Initialize(Switch, gAddr + s * 8, 8);
                        int gCount = Group.Count;
                        VoidPtr bAddr = BaseAddress + Group.DataOffset;
                        for (int g = 0; g < gCount; g++)
                            new MoveDefBoneIndexNode().Initialize(Group, bAddr + g * 4, 4);
                    }
                }
            }
            if (Children.Count > 0)
            for (int i = 0; i < DefaultsCount; i++)
            {
                FDefModelDisplayDefaults* def = (FDefModelDisplayDefaults*)(defaults + i * 8);
                for (int x = 0; x < ((DefaultsOffset == 0 ? _offset : DefaultsOffset) - EntryOffset) / 4; x++)
                    if (Children[x].Children.Count > 0)
                        (Children[x].Children[def->_switchIndex] as MoveDefBoneSwitchNode).defaultGroup = def->_defaultGroup;
            }
        }

        public override int OnCalculateSize(bool force)
        {
            int size = 16;
            _lookupCount = (Children.Count > 0 ? 1 : 0);

            int defCount = 0;
            foreach (MoveDefModelVisRefNode r in Children)
            {
                size += 4;

                if (r.Children.Count > 0)
                    _lookupCount++;
                
                foreach (MoveDefBoneSwitchNode b in r.Children)
                {
                    size += 8 + (b.defaultGroup < 0 ? 0 : 8);

                    if (b.defaultGroup >= 0)
                        defCount++;

                    if (b.Children.Count > 0)
                        _lookupCount++;

                    foreach (MoveDefModelVisGroupNode o in b.Children)
                    {
                        size += 8 + o.Children.Count * 4;
                        if (o.Children.Count > 0)
                            _lookupCount++;
                    }
                }
            }

            if (defCount > 0)
                _lookupCount++;

            return size;
        }

        public override void OnRebuild(VoidPtr address, int length, bool force)
        {
            _lookupOffsets = new List<VoidPtr>();

            int mainOff = 0, defOff = 0, offOff = 0, swtchOff = 0, grpOff = 0;
            foreach (MoveDefModelVisRefNode r in Children)
            {
                defOff += 4;
                foreach (MoveDefBoneSwitchNode b in r.Children)
                {
                    offOff += 8;
                    if (b.defaultGroup >= 0)
                        mainOff += 8;
                    foreach (MoveDefModelVisGroupNode o in b.Children)
                    {
                        swtchOff += 8;
                        grpOff += o.Children.Count * 4;
                    }
                }
            }

            //bones
            //groups
            //switches
            //offsets
            //defaults
            //header

            bint* boneAddr = (bint*)address;
            FDefListOffset* groupLists = (FDefListOffset*)((VoidPtr)boneAddr + grpOff);
            FDefListOffset* switchLists = (FDefListOffset*)((VoidPtr)groupLists + swtchOff);
            bint* offsets = (bint*)((VoidPtr)switchLists + offOff);
            FDefModelDisplayDefaults* defaults = (FDefModelDisplayDefaults*)((VoidPtr)offsets + defOff);
            FDefModelDisplay* header = (FDefModelDisplay*)((VoidPtr)defaults + mainOff);

            _rebuildAddr = header;

            header->_entryOffset = (int)offsets - (int)RebuildBase;

            _lookupOffsets.Add(header->_entryOffset.Address);

            header->_entryCount = Children[0].Children.Count; //Children 1 child count will be the same

            int defCount = 0;
            foreach (MoveDefModelVisRefNode r in Children)
            {
                r._rebuildAddr = offsets;
                if (r.Children.Count > 0)
                {
                    *offsets = (int)switchLists - (int)RebuildBase;
                    _lookupOffsets.Add(offsets);
                }
                offsets++;
                foreach (MoveDefBoneSwitchNode b in r.Children)
                {
                    b._rebuildAddr = switchLists;
                    if (b.defaultGroup >= 0)
                    {
                        defCount++;
                        defaults->_switchIndex = b.Index;
                        (defaults++)->_defaultGroup = b.defaultGroup;
                    }
                    switchLists->_listCount = b.Children.Count;
                    if (b.Children.Count > 0)
                    {
                        switchLists->_startOffset = (int)groupLists - (int)RebuildBase;
                        _lookupOffsets.Add(switchLists->_startOffset.Address);
                    }
                    else
                        switchLists->_startOffset = 0;
                    switchLists++;
                    foreach (MoveDefModelVisGroupNode o in b.Children)
                    {
                        o._rebuildAddr = groupLists;
                        groupLists->_listCount = o.Children.Count;
                        if (o.Children.Count > 0)
                        {
                            groupLists->_startOffset = (int)boneAddr - (int)RebuildBase;
                            _lookupOffsets.Add(groupLists->_startOffset.Address);
                        }
                        else
                            groupLists->_startOffset = 0;
                        groupLists++;
                        foreach (MoveDefBoneIndexNode bone in o.Children)
                        {
                            bone._rebuildAddr = boneAddr;
                            *boneAddr++ = bone.boneIndex;
                        }
                    }
                }
            }

            if (defCount > 0)
            {
                header->_defaultsOffset = (int)offsets - (int)RebuildBase;
                _lookupOffsets.Add(header->_defaultsOffset.Address);
            }
            else
                header->_defaultsOffset = 0;
            header->_defaultsCount = defCount;
        }
    }

    public unsafe class MoveDefModelVisRefNode : MoveDefEntry
    {
        internal bint* Header { get { return (bint*)WorkingUncompressed.Address; } }
        public override ResourceType ResourceType { get { return ResourceType.MDefMdlVisRef; } }

        internal int i = 0;

        [Category("Offset Entry")]
        public int DataOffset { get { return i; } }

        public override bool OnInitialize()
        {
            base.OnInitialize();
            i = *Header;
            if (_name == null)
            {
                _externalEntry = _root.TryGetExternal(DataOffset);
                if (_externalEntry != null && !_extOverride)
                {
                    _name = _externalEntry.Name;
                    _externalEntry._refs.Add(this);
                }
            }

            if (_name == null)
                _name = "Offset" + Index;

            return false;
        }
    }

    public unsafe class MoveDefBoneSwitchNode : MoveDefEntry
    {
        internal FDefListOffset* Header { get { return (FDefListOffset*)WorkingUncompressed.Address; } }
        public override ResourceType ResourceType { get { return ResourceType.MDefMdlVisSwitch; } }

        internal int i = 0;

        int offset, count;

        public int defaultGroup = -1;

        [Category("Bone Group Switch")]
        public int DataOffset { get { return offset; } }
        [Category("Bone Group Switch")]
        public int Count { get { return count; } }
        [Category("Bone Group Switch")]
        public int DefaultGroup { get { return defaultGroup; } set { defaultGroup = value; SignalPropertyChange(); } }

        public override bool OnInitialize()
        {
            base.OnInitialize();
            offset = Header->_startOffset;
            count = Header->_listCount;
            return false;
        }
    }

    public unsafe class MoveDefModelVisGroupNode : MoveDefEntry
    {
        internal FDefListOffset* Header { get { return (FDefListOffset*)WorkingUncompressed.Address; } }
        public override ResourceType ResourceType { get { return ResourceType.MDefMdlVisGroup; } }

        internal int i = 0;

        int offset, count;

        [Category("Bone Group")]
        public int DataOffset { get { return offset; } }
        [Category("Bone Group")]
        public int Count { get { return count; } }

        public override bool OnInitialize()
        {
            base.OnInitialize();
            offset = Header->_startOffset;
            count = Header->_listCount;
            return false;
        }
    }
}