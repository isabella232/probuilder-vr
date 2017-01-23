﻿using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputNew;
using UnityEngine.Experimental.EditorVR;
using UnityEngine.Experimental.EditorVR.Menus;
using UnityEngine.Experimental.EditorVR.Tools;
using UnityEngine.Experimental.EditorVR.Utilities;
using ProBuilder2.Common;

namespace ProBuilder2.VR
{
	/**
	 * Translates faces along their normal.
	 */
	[MainMenuItem("Move Elements", "ProBuilder", "Translate selected mesh elements.")]
	public class TranslateElementTool : MonoBehaviour, ITool, IStandardActionMap, IUsesRayOrigin, IUsesRaycastResults, ISetHighlight
	{
		[SerializeField] private AudioClip m_DragTick;

		enum CreateState
		{
			Start,
			Finish
		}

		public Transform rayOrigin { get; set; }
	   	public Func<Transform, GameObject> getFirstGameObject { get; set; }
		public Action<GameObject, bool> setHighlight { get; set; }

		private HighlightElementsModule m_HighlightModule = null;
		private VRAudioModule m_AudioModule = null;
		const float MAX_TRANSLATE_DISTANCE = 100f;
		private static readonly Vector3 VECTOR3_ONE = Vector3.one;
		private float m_SnapIncrement = Snapping.DEFAULT_INCREMENT;

	   	private CreateState m_State = CreateState.Start;
	   	private bool m_Dragging = false;
	   	private Vector3 m_DragOrigin = Vector3.zero;
		private Vector3 m_DragDirection = Vector3.zero;
		private Vector3 m_DraggedPoint = Vector3.zero;
		private Vector3 m_PreviousLinePosition = Vector3.zero;
		private Vector3 m_PreviousVertexTranslation = Vector3.zero;
		
		private pb_Object m_Object;
		private pb_Face m_Face;
		private IEnumerable<int> m_SelectedIndices;
		private Vector3[] m_Positions;
		private Vector3[] m_SettingPositions;

		void Start()
		{
			m_HighlightModule = U.Object.CreateGameObjectWithComponent<HighlightElementsModule>();
			m_AudioModule = U.Object.CreateGameObjectWithComponent<VRAudioModule>();
		}

		void OnDestroy()
		{
			U.Object.Destroy(m_HighlightModule.gameObject);
			U.Object.Destroy(m_AudioModule.gameObject);
		}

		public void ProcessInput(ActionMapInput input, Action<InputControl> consumeControl)
		{
			if(m_State == CreateState.Start)
			{
				HandleStart( (Standard) input, consumeControl );
			}
			else if(m_State == CreateState.Finish)
			{
				HandleFinish( (Standard) input, consumeControl );
			}
		}

		private void HandleStart(Standard input, Action<InputControl> consumeControl)
		{
			GameObject first = getFirstGameObject(rayOrigin);

			if(first == null)	
				return;

			pb_Object pb = first.GetComponent<pb_Object>();
			
			if(pb == null)	
				return;

			pb_RaycastHit hit;

			if( pb_HandleUtility.FaceRaycast(new Ray(rayOrigin.position, rayOrigin.forward), pb, out hit) )
			{
				m_HighlightModule.SetFaceHighlight(pb, new pb_Face[] { pb.faces[hit.face] } );
				setHighlight(pb.gameObject, false);

				consumeControl(input.action);

				if(!input.action.wasJustPressed)
					return;

				m_Object = pb;
				m_Face = pb.faces[hit.face];
				m_SelectedIndices = pb.sharedIndices.AllIndicesWithValues(m_Face.distinctIndices);

				m_DragOrigin = pb.transform.TransformPoint(hit.point);
				m_DragDirection = pb.transform.TransformDirection(hit.normal);
				m_DragDirection.Normalize();

				m_State = CreateState.Finish;

				m_Positions = new Vector3[pb.vertexCount];
				m_SettingPositions = new Vector3[pb.vertexCount];
				System.Array.Copy(pb.vertices, m_Positions, pb.vertexCount);
				System.Array.Copy(pb.vertices, m_SettingPositions, pb.vertexCount);

				m_Object.ToMesh();
			}
			else
			{
				m_HighlightModule.SetFaceHighlight(pb, null);
			}
		}

		private void HandleFinish(Standard input, Action<InputControl> consumeControl)
		{
			// Ready for next object to be created
			if (input.action.wasJustReleased)
			{
				m_Dragging = false;
				m_State = CreateState.Start;
				m_HighlightModule.SetFaceHighlight(m_Object, null);
				m_Object.ToMesh();
				m_Object.Refresh();
			}
			else
			{
				m_DraggedPoint = VRMath.CalculateNearestPointRayRay(m_DragOrigin, m_DragDirection, rayOrigin.position, rayOrigin.forward);

				if(!m_Dragging)
				{
					m_PreviousLinePosition = m_DraggedPoint;	
					m_Dragging = true;
				}

				Vector3 smoothedDragPoint = Vector3.Lerp(m_PreviousLinePosition, m_DraggedPoint, .5f);
				Vector3 localDragOrigin = m_Object.transform.InverseTransformPoint(m_DragOrigin);
				Vector3 localDraggedPoint = m_Object.transform.InverseTransformPoint(smoothedDragPoint);
				Vector3 vertexTranslation = localDraggedPoint - localDragOrigin;
				m_PreviousLinePosition = m_DraggedPoint;

				if(vertexTranslation.magnitude > MAX_TRANSLATE_DISTANCE)
					vertexTranslation = vertexTranslation.normalized * MAX_TRANSLATE_DISTANCE;

				vertexTranslation = Snapping.Snap(vertexTranslation, m_SnapIncrement, VECTOR3_ONE);

				if(vertexTranslation != m_PreviousVertexTranslation)
				{
					m_PreviousVertexTranslation = vertexTranslation;
					m_AudioModule.Play(m_DragTick);
				}

				foreach(int ind in m_SelectedIndices)
					m_SettingPositions[ind] = m_Positions[ind] + vertexTranslation;

				m_Object.SetVertices(m_SettingPositions);
				m_Object.msh.vertices = m_SettingPositions;
				m_Object.RefreshUV();
				m_HighlightModule.UpdateVertices(m_Object);
			}
			
			setHighlight(m_Object.gameObject, false);
			consumeControl(input.action);
		}
	}
}