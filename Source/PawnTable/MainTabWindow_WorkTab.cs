﻿// Karel Kroeze
// PawnTable_WorkTab.cs
// 2017-05-22

using System;
using System.Collections.Generic;
using System.Reflection;
using Harmony;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using static WorkTab.Constants;
using static WorkTab.InteractionUtilities;
using static WorkTab.Resources;

namespace WorkTab
{
    public class MainTabWindow_WorkTab: MainTabWindow_PawnTable
    {
        protected override PawnTableDef PawnTableDef => PawnTableDefOf.Work;
        private static FieldInfo _tableFieldInfo;
        private static List<int> _selectedHours = TimeUtilities.WholeDay;
        private static int _visibleHour = -1;
        public static List<int> SelectedHours => _selectedHours;
        public static int VisibleHour => _visibleHour;

        public static void AddSelectedHour( int hour )
        {
            if (!_selectedHours.Contains( hour ))
                _selectedHours.Add( hour );
            _visibleHour = hour;
        }

        public static void RemoveSelectedHour( int hour )
        {
            if ( _selectedHours.Contains( hour ) )
                _selectedHours.Remove( hour );

            if ( _visibleHour == hour )
                _visibleHour = hour;
        }

        public static void SelectCurrentHour()
        {
            _selectedHours = new List<int>();
            AddSelectedHour(GenLocalDate.HourOfDay(Find.VisibleMap) );
        }

        public static void SelectWholeDay()
        {
            _selectedHours = TimeUtilities.WholeDay;
            _visibleHour = -1;
        }

        static MainTabWindow_WorkTab()
        {
            _tableFieldInfo = typeof(MainTabWindow_PawnTable).GetField("table",
                                                                          BindingFlags.Instance | BindingFlags.NonPublic);
            if (_tableFieldInfo == null)
                throw new NullReferenceException("table field not found!");
        }
        public MainTabWindow_WorkTab() { _instance = this; }

        private static MainTabWindow_WorkTab _instance;
        public static MainTabWindow_WorkTab Instance => _instance;

        public override void PostOpen()
        {
            base.PostOpen();
            Find.World.renderer.wantedMode = WorldRenderMode.None;
            RebuildTable();
        }
        
        public PawnTable Table
        {
            get { return _tableFieldInfo.GetValue(this) as PawnTable; }
            private set { _tableFieldInfo.SetValue(this, value); }
        }

        public static void RebuildTable()
        {
            if ( _instance == null )
            {
                Logger.Debug( "calling RebuildTable on a null instance" );
                return;
            }

            // get columns
            var columns = Columns; 
            
            // update alternating vertical positions
            bool moveNextDown = false;
            foreach ( PawnColumnDef columnDef in columns )
            {
                var worker = columnDef.Worker as IAlternatingColumn;
                if ( worker != null )
                {
                    worker.MoveDown = moveNextDown;
                    moveNextDown = !moveNextDown;
                }
                else
                {
                    moveNextDown = false;
                }
            }

            // rebuild table
            Instance.Table = new PawnTable( columns, () => Instance.Pawns, 998, UI.screenWidth - (int)(Instance.Margin * 2f), 0,
                                   (int)(UI.screenHeight - 35 - Instance.ExtraBottomSpace - Instance.ExtraTopSpace - Instance.Margin * 2f));

            // force recache of timeBarRect
            Instance._timeBarRect = default( Rect );
        }

        private static List<PawnColumnDef> Columns
        {
            get
            {
                // get clean copy of base columns
                List<PawnColumnDef> columns = new List<PawnColumnDef>(_instance.PawnTableDef.columns);

                // add workgiver columns for expanded worktypes
                var templist = new List<PawnColumnDef>(columns);
                foreach (PawnColumnDef column in templist)
                {
                    var expandable = column.Worker as IExpandableColumn;
                    if (expandable != null && expandable.Expanded)
                    {
                        var index = columns.IndexOf(column);
                        columns.InsertRange(index + 1, expandable.ChildColumns);
                    }
                }

                return columns;
            }
        }

        public override void DoWindowContents( Rect rect )
        {
            base.DoWindowContents( rect );
            if (Event.current.type == EventType.Layout)
                return;

            DoToggleButtons( rect );
            DoPriorityLabels( rect );
            DoTimeBar( rect );
        }

        private void DoTimeBar( Rect canvas )
        {
            Rect bar = TimeBarRect;
            Widgets.DrawBox( bar );
        }


        private Rect _timeBarRect = default( Rect );
        public Rect TimeBarRect
        {
            get
            {
                if ( _timeBarRect == default( Rect ) )
                {
                    var widths = Traverse.Create( Table ).Field( "cachedColumnWidths" ).GetValue<List<float>>();
                    var columns = Table.ColumnsListForReading;
                    float start = 0;
                    float width = 0;

                    // loop over columns, initially add any column that is not a workbox to the start, but not after we've seen a workbox.
                    // Add widths for workboxes to width. 
                    // NOTE: This assumes a continguous block of workboxes!
                    for ( int i = 0; i < columns.Count; i++ )
                    {
                        var column = columns[i].Worker;
                        if ( column is PawnColumnWorker_WorkType || column is PawnColumnWorker_WorkGiver )
                            width += widths[i];
                        else if ( width == 0 )
                            start += widths[i];
                    }

                    // build the rect
                    _timeBarRect = new Rect( start, 
                                             windowRect.height - TimeBarHeight - base.ExtraBottomSpace, 
                                             width,
                                             TimeBarHeight );

                    Logger.Debug( "created time bar rect: " + _timeBarRect );
                }

                return _timeBarRect;
            }
        }

        private void DoPriorityLabels( Rect canvas )
        {
            Rect priorityLabelRect = new Rect( canvas.width / 3f - PriorityLabelSize.x / 2f,
                                               canvas.yMin,
                                               PriorityLabelSize.x,
                                               PriorityLabelSize.y );

            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Text.Anchor = TextAnchor.UpperCenter;
            Text.Font = GameFont.Tiny;

            Widgets.Label( priorityLabelRect, "<= " + "HigherPriority".Translate());

            priorityLabelRect.x += canvas.width / 3f;
            Widgets.Label( priorityLabelRect, "LowerPriority".Translate() + " =>");

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DoToggleButtons( Rect canvas )
        {
            Rect rect = new Rect( canvas.xMax - 30f, canvas.yMin, 30f, 30f );

            ButtonImageToggle(() => PriorityManager.Get.UseWorkPriorities, (val) => PriorityManager.Get.UseWorkPriorities = val, rect,
                               "WorkTab.PrioritiesDetailed".Translate(), PrioritiesDetailed,
                               "WorkTab.PrioritiesSimple".Translate(), PrioritiesSimple );
            rect.x -= 30f + Margin;

            ButtonImageToggle( () => PriorityManager.Get.UseScheduler, (val) => PriorityManager.Get.UseScheduler = val, rect,
                               "WorkTab.PrioritiesTimed".Translate(), PrioritiesTimed,
                               "WorkTab.PrioritiesWholeDay".Translate(), PrioritiesWholeDay);
            rect.x -= 30f + Margin;
        }

        protected override float ExtraBottomSpace => base.ExtraBottomSpace + TimeBarHeight;
        protected override float ExtraTopSpace => base.ExtraTopSpace + Constants.ExtraTopSpace;
    }
}