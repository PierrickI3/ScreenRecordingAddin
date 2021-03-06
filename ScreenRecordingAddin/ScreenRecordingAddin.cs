﻿using ININ.IceLib.Connection;
using ININ.IceLib.QualityManagement;
using ININ.InteractionClient.AddIn;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Instrumentation;
using System.Windows.Forms;
using MessageBox = System.Windows.Forms.MessageBox;

namespace ScreenRecording.ScreenRecordingAddin
{
    public class ScreenRecordingAddin : QueueMonitor
    {
        private IServiceProvider _serviceProvider;
        private Session _session;
        private QualityManagementManager _qualityManagementManager;
        
        private const string SCREEN_RECORDING_ATTRIBUTE_GUID = "ScreenRecording_Guids";

        protected override IEnumerable<string> Attributes
        {
            get
            {
                return new string[]
                {
                    InteractionAttributes.InteractionType,
                    InteractionAttributes.State
                };
            }
        }

        protected override IEnumerable<string> SupportingAttributes
        {
            get
            {
                return new string[]
                {
                    SCREEN_RECORDING_ATTRIBUTE_GUID
                };
            }
        }
        public ScreenRecordingAddin()
        {
            
        }

        #region Load/Unload

        protected override void OnLoad(IServiceProvider serviceProvider)
        {
            try
            {
                base.OnLoad(serviceProvider);

                // Store service provider reference
                _serviceProvider = serviceProvider;

                // Get Session
                _session = _serviceProvider.GetService(typeof(Session)) as Session;
                if (_session == null)
                {
                    throw new InstanceNotFoundException("Failed to get the IceLib Session from the service provider!");
                }

                _qualityManagementManager = QualityManagementManager.GetInstance(_session);

                Trace.Note("Screen Recording Addin: Started");
            }
            catch (Exception ex)
            {
                Trace.Error("Screen Recording Addin: " + ex.ToString());
                MessageBox.Show("Screen Recording Addin initialization failed! " + ex.Message + "\n\nPlease contact your system administrator.", "Critical error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void OnUnload()
        {
            base.OnUnload();
        }

        #endregion

        #region Interaction Events

        protected override void InteractionAdded(IInteraction interaction)
        {
            InteractionChanged(interaction);
        }
        
        protected override void InteractionChanged(IInteraction interaction)
        {
            if (!IsGenericObject(interaction))
            {
                return; // Not a generic object. Exiting.
            }

 	        if (interaction.GetAttribute(InteractionAttributes.State) == InteractionAttributeValues.State.Connected)
            {
                StartScreenRecording(interaction);
            }
            else if (interaction.GetAttribute(InteractionAttributes.State) == InteractionAttributeValues.State.ExternalDisconnect || interaction.GetAttribute(InteractionAttributes.State) == InteractionAttributeValues.State.InternalDisconnect)
            {
                // Need to stop screen recording on disconnect
                StopScreenRecording(interaction);
            }
            
        }

        protected override void InteractionRemoved(IInteraction interaction)
        {
            if (!IsGenericObject(interaction))
            {
                return; // Not a generic object. Exiting.
            }

            // Stop the screen recording if the interaction was transferred
            StopScreenRecording(interaction);
        }

        #endregion

        private bool IsGenericObject(IInteraction interaction)
        {
            return interaction.GetAttribute(InteractionAttributes.InteractionType) == InteractionAttributeValues.InteractionType.Generic;
        }

        #region Screen Recording

        private void StartScreenRecording(IInteraction interaction)
        {
            if (interaction.GetAttribute(SCREEN_RECORDING_ATTRIBUTE_GUID).Length > 0)
            {
                return; // Screen Recording has already started for this interaction. Exiting.
            }
            Trace.Note("Screen Recording Addin: Starting screen recording");
            var screenRecorder = new ScreenRecorder(_qualityManagementManager);
            var guids = screenRecorder.StartRecording(_session.UserId);
            foreach (var guid in guids)
            {
                Trace.Verbose("Screen Recording Addin: Guid: " + guid.ToString());
            }

            var guidsString = String.Join("|", guids);

            interaction.SetAttribute(SCREEN_RECORDING_ATTRIBUTE_GUID, guidsString);
            Trace.Verbose("Screen Recording Addin: GUIDs: " + guidsString);
        }

        private void OnStartRecordingInitiated(object sender, StartRecordingCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                Trace.Error("Screen Recording Addin: " + e.Error.ToString());
                MessageBox.Show("Screen Recording failed. " + e.Error.Message + "\n\nPlease contact your system administrator.", "Critical error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            var interaction = (IInteraction)e.UserState;
            Trace.Verbose("Screen Recording Addin: Interaction Id: " + interaction.InteractionId);
            foreach (var guid in e.RecordingIds)
            {
                Trace.Verbose("Guid: " + guid.ToString());
            }
            var guids = String.Join("|", e.RecordingIds);

            interaction.SetAttribute(SCREEN_RECORDING_ATTRIBUTE_GUID, guids);
            Trace.Verbose("Screen Recording Addin: GUIDs: " + guids);
        }

        private void StopScreenRecording(IInteraction interaction)
        {
            if (interaction.GetAttribute(SCREEN_RECORDING_ATTRIBUTE_GUID).Length == 0)
            {
                return; // No Screen Recording initiated for this interaction. Exiting.
            }

            var screenRecorder = new ScreenRecorder(_qualityManagementManager);
            
            // Parse GUIDs and avoid an exception if one of them is invalid.
            var guids = interaction.GetAttribute(SCREEN_RECORDING_ATTRIBUTE_GUID).Split('|')
                .Where(g => { Guid temp; return Guid.TryParse(g, out temp); })
                .Select(g => Guid.Parse(g))
                .ToArray();
            
            screenRecorder.StopRecordingAsync(_session.UserId, guids, OnScreenRecordingStopped, interaction);
        }

        private void OnScreenRecordingStopped(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                MessageBox.Show("Failed to stop Screen Recording. " + e.Error.Message + "\n\nPlease contact your system administrator.", "Critical error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            var interaction = (IInteraction)e.UserState;
            interaction.SetAttribute(SCREEN_RECORDING_ATTRIBUTE_GUID, String.Empty);
        }

        #endregion
        
    }
}
