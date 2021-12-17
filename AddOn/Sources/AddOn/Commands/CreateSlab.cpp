#include "CreateSlab.hpp"
#include "ResourceIds.hpp"
#include "ObjectState.hpp"
#include "SchemaDefinitionBuilder.hpp"
#include "Utility.hpp"
#include "Objects/Polyline.hpp"
#include "FieldNames.hpp"
#include "TypeNameTables.hpp"
#include "AngleData.h"


namespace AddOnCommands {


GSErrCode CreateNewSlab (API_Element& slab, API_ElementMemo& slabMemo)
{
	return ACAPI_Element_Create (&slab, &slabMemo);
}


GSErrCode ModifyExistingSlab (API_Element& slab, API_Element& mask, API_ElementMemo& slabMemo, GS::UInt64 memoMask)
{
	return ACAPI_Element_Change (&slab, &mask, &slabMemo, memoMask, true);
}


GSErrCode GetSlabFromObjectState (const GS::ObjectState&	os, 
								  API_Element&				element, 
								  API_Element&				mask, 
								  API_ElementMemo&			slabMemo,
								  GS::UInt64&				memoMask)
{
	GSErrCode err;

	// The guid of the slab
	GS::UniString guidString;
	os.Get (ElementIdFieldName, guidString);
	element.header.guid = APIGuidFromString (guidString.ToCStr ());
	element.header.typeID = API_SlabID;

	err = Utility::GetBaseElementData (element, &slabMemo);
	if (err != NoError)
		return err;

	memoMask = APIMemoMask_Polygon | APIMemoMask_SideMaterials | APIMemoMask_EdgeTrims;

	ACAPI_ELEMENT_MASK_SET (mask, API_SlabType, poly.nSubPolys);
	ACAPI_ELEMENT_MASK_SET (mask, API_SlabType, poly.nCoords);
	ACAPI_ELEMENT_MASK_SET (mask, API_SlabType, poly.nArcs);
	ACAPI_ELEMENT_MASK_SET (mask, API_SlabType, level);
	ACAPI_ELEMENT_MASK_SET (mask, API_Elem_Head, floorInd);

	// The shape of the slab
	Objects::ElementShape slabShape;

	if (os.Contains (Slab::ShapeFieldName)) {
		os.Get (Slab::ShapeFieldName, slabShape);
		element.slab.poly.nSubPolys = slabShape.SubpolyCount ();
		element.slab.poly.nCoords = slabShape.VertexCount ();
		element.slab.poly.nArcs = slabShape.ArcCount ();

		slabShape.SetToMemo (slabMemo);
	}

	// The floor index and level of the slab
	if (os.Contains (FloorIndexFieldName)) {
		os.Get (FloorIndexFieldName, element.header.floorInd);
		Utility::SetStoryLevel (slabShape.Level (), element.header.floorInd, element.slab.level);
	} else {
		Utility::SetStoryLevelAndFloor (slabShape.Level (), element.header.floorInd, element.slab.level);
	}

	// The structure of the slab
	if (os.Contains (Slab::StructureFieldName)) {
		GS::UniString structureName;
		os.Get (Slab::StructureFieldName, structureName);

		GS::Optional<API_ModelElemStructureType> type = structureTypeNames.FindValue (structureName);
		if (type.HasValue ())
			element.slab.modelElemStructureType = type.Get ();

		ACAPI_ELEMENT_MASK_SET (mask, API_SlabType, modelElemStructureType);
	}

	// The thickness of the slab
	if (os.Contains (Slab::ThicknessFieldName)) {
		os.Get (Slab::ThicknessFieldName, element.slab.thickness);
		ACAPI_ELEMENT_MASK_SET (mask, API_SlabType, thickness);
	}

	// The structure of the slab
	if (os.Contains (Slab::ReferencePlaneLocationFieldName)) {
		GS::UniString refPlaneLocationName;
		os.Get (Slab::ReferencePlaneLocationFieldName, refPlaneLocationName);

		GS::Optional<API_SlabReferencePlaneLocationID> id = referencePlaneLocationNames.FindValue (refPlaneLocationName);
		if (id.HasValue ())
			element.slab.referencePlaneLocation = id.Get ();

		ACAPI_ELEMENT_MASK_SET (mask, API_SlabType, referencePlaneLocation);
	}

	// The edge type of the slab
	API_EdgeTrimID edgeType = APIEdgeTrim_Perpendicular;
	if (os.Contains (Slab::EdgeAngleTypeFieldName)) {
		GS::UniString edgeTypeName;
		os.Get (Slab::EdgeAngleTypeFieldName, edgeTypeName);

		GS::Optional<API_EdgeTrimID> type = edgeAngleTypeNames.FindValue (edgeTypeName);
		if (type.HasValue ())
			edgeType = type.Get ();
	}

	// The edge angle of the slab
	GS::Optional<double> edgeAngle;
	if (os.Contains (Slab::EdgeAngleFieldName)) {
		double angle = 0;
		os.Get (Slab::EdgeAngleFieldName, angle);
		edgeAngle = angle;
	}

	// Setting sidematerials and edge angles
	BMhKill ((GSHandle*)&slabMemo.edgeTrims);
	BMhKill ((GSHandle*)&slabMemo.edgeIDs);
	BMpFree (reinterpret_cast<GSPtr> (slabMemo.sideMaterials));

	slabMemo.edgeTrims = (API_EdgeTrim**)BMAllocateHandle ((element.slab.poly.nCoords + 1) * sizeof (API_EdgeTrim), ALLOCATE_CLEAR, 0);
	slabMemo.sideMaterials = (API_OverriddenAttribute*)BMAllocatePtr ((element.slab.poly.nCoords + 1) * sizeof (API_OverriddenAttribute), ALLOCATE_CLEAR, 0);
	for (Int32 k = 1; k <= element.slab.poly.nCoords; ++k) {
		slabMemo.sideMaterials [k] = element.slab.sideMat;

		(*(slabMemo.edgeTrims)) [k].sideType = edgeType;
		(*(slabMemo.edgeTrims)) [k].sideAngle = (edgeAngle.HasValue ()) ? edgeAngle.Get () : PI / 2;
	}

	return NoError;
}


GS::String CreateSlab::GetNamespace () const
{
	return CommandNamespace;
}


GS::String CreateSlab::GetName () const
{
	return CreateSlabCommandName;
}
	
		
GS::Optional<GS::UniString> CreateSlab::GetSchemaDefinitions () const
{
	Json::SchemaDefinitionBuilder builder;
	builder.Add (Json::SchemaDefinitionProvider::SlabDataSchema ());
	builder.Add (Json::SchemaDefinitionProvider::ElementIdsSchema ());
	return builder.Build();
}


GS::Optional<GS::UniString>	CreateSlab::GetInputParametersSchema () const
{
	return GS::NoValue;
	/*	//TMP for DEV
	return R"(
		{
			"type": "object",
			"properties" : {
				"slabs": {
					"type": "array",
					"items": { "$ref": "#/definitions/SlabData" }
				}
			},
			"additionalProperties" : false,
			"required" : [ "slabs" ]
		}
	)"; */
}


GS::Optional<GS::UniString> CreateSlab::GetResponseSchema () const
{
	return GS::NoValue;
	/*	//TMP for DEV
	return R"(
		{
			"type": "object",
			"properties" : {
				"elementIds": { "$ref": "#/definitions/ElementIds" }
			},
			"additionalProperties" : false,
			"required" : [ "elementIds" ]
		}
	)";	*/
}


API_AddOnCommandExecutionPolicy CreateSlab::GetExecutionPolicy () const
{
	return API_AddOnCommandExecutionPolicy::ScheduleForExecutionOnMainThread; 
}


GS::ObjectState CreateSlab::Execute (const GS::ObjectState& parameters, GS::ProcessControl& /*processControl*/) const
{
	GS::ObjectState result;
	GSErrCode		errCode;

	GS::Array<GS::ObjectState> slabs;
	parameters.Get (SlabsFieldName, slabs);

	const auto& listAdder = result.AddList<GS::UniString> (ElementIdsFieldName);

	errCode = ACAPI_CallUndoableCommand ("CreateSpeckleSlab", [&] () -> GSErrCode {
		GSErrCode err = NoError;

		for (const GS::ObjectState& slabOs : slabs) {

			API_Element slab {};
			API_Element	slabMask {};
			API_ElementMemo slabMemo {};
			GS::UInt64 memoMask = 0;

			err = GetSlabFromObjectState (slabOs, slab, slabMask, slabMemo, memoMask);
			if (err != NoError) {
				ACAPI_DisposeElemMemoHdls (&slabMemo);
				continue;
			}

			bool slabExists = Utility::ElementExists (slab.header.guid);
			if (slabExists) {
				err = ModifyExistingSlab (slab, slabMask, slabMemo, memoMask);
			}
			else {
				err = CreateNewSlab (slab, slabMemo);
			}

			ACAPI_DisposeElemMemoHdls (&slabMemo);

			if (err != NoError)
				continue;

			GS::UniString elemId = APIGuidToString (slab.header.guid);
			listAdder (elemId);

		}
		return err;
	});

	return result;
}


void CreateSlab::OnResponseValidationFailed (const GS::ObjectState& /*response*/) const
{
}


}