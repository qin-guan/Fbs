/* eslint-disable */
import * as types from './graphql';
import type { TypedDocumentNode as DocumentNode } from '@graphql-typed-document-node/core';

/**
 * Map of all GraphQL operations in the project.
 *
 * This map has several performance disadvantages:
 * 1. It is not tree-shakeable, so it will include all operations in the project.
 * 2. It is not minifiable, so the string of a GraphQL query will be multiple times inside the bundle.
 * 3. It does not support dead code elimination, so it will add unused operations.
 *
 * Therefore it is highly recommended to use the babel or swc plugin for production.
 * Learn more about it here: https://the-guild.dev/graphql/codegen/plugins/presets/preset-client#reducing-bundle-size
 */
type Documents = {
    "\n    query me {\n      me {\n        phone\n      }\n    }\n  ": typeof types.MeDocument,
    "\n    query meWithName {\n      me {\n        name\n        phone\n      }\n    }\n  ": typeof types.MeWithNameDocument,
    "\n    query meWithEverything {\n      me {\n        id\n        unit\n        name\n        phone\n        telegramChatId\n        row\n      }\n    }\n  ": typeof types.MeWithEverythingDocument,
    "\n    query facilities {\n      facilities {\n        name\n      }\n    }\n  ": typeof types.FacilitiesDocument,
    "\n    query slots($facility: String!, $startDate: DateTime!, $endDate: DateTime!) {\n      facilities(name: $facility) {\n        name\n        availableTimeSlots(start: $startDate, end: $endDate) {\n          startDateTime\n          endDateTime\n          booking {\n            conduct\n            description\n            pocName\n            pocPhone\n            user {\n              unit\n            }\n          }\n        }\n      }\n    }\n  ": typeof types.SlotsDocument,
    "\n    query booking($id: UUID!) {\n      booking(id: $id) {\n        id\n        conduct\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n    }\n  ": typeof types.BookingDocument,
    "\n    query bookings($userPhone: String, $startsAfter: DateTime) {\n      bookings(userPhone: $userPhone, startsAfter: $startsAfter) {\n        id\n        conduct\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n    }\n  ": typeof types.BookingsDocument,
    "\n    mutation deleteBooking($id: UUID!) {\n      deleteBooking(id: $id)\n    }\n  ": typeof types.DeleteBookingDocument,
    "\n    mutation updateBooking(\n      $id: UUID!\n      $conduct: String!\n      $description: String!\n      $pocName: String!\n      $pocPhone: String!\n    ) {\n\n      updateBooking(\n        id: $id\n        conduct: $conduct\n        description: $description\n        pocName: $pocName\n        pocPhone: $pocPhone\n      ) {\n        id\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n\n    }\n  ": typeof types.UpdateBookingDocument,
    "\n    mutation insertBooking(\n      $conduct: String!\n      $description: String!\n      $facilityName: String!\n      $pocName: String!\n      $pocPhone: String!\n      $startDateTime: DateTime!\n      $endDateTime: DateTime!\n    ) {\n\n      insertBooking(\n        conduct: $conduct\n        description: $description\n        facilityName: $facilityName\n        pocName: $pocName\n        pocPhone: $pocPhone\n        startDateTime: $startDateTime\n        endDateTime: $endDateTime\n      ) {\n        id\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n\n    }\n  ": typeof types.InsertBookingDocument,
};
const documents: Documents = {
    "\n    query me {\n      me {\n        phone\n      }\n    }\n  ": types.MeDocument,
    "\n    query meWithName {\n      me {\n        name\n        phone\n      }\n    }\n  ": types.MeWithNameDocument,
    "\n    query meWithEverything {\n      me {\n        id\n        unit\n        name\n        phone\n        telegramChatId\n        row\n      }\n    }\n  ": types.MeWithEverythingDocument,
    "\n    query facilities {\n      facilities {\n        name\n      }\n    }\n  ": types.FacilitiesDocument,
    "\n    query slots($facility: String!, $startDate: DateTime!, $endDate: DateTime!) {\n      facilities(name: $facility) {\n        name\n        availableTimeSlots(start: $startDate, end: $endDate) {\n          startDateTime\n          endDateTime\n          booking {\n            conduct\n            description\n            pocName\n            pocPhone\n            user {\n              unit\n            }\n          }\n        }\n      }\n    }\n  ": types.SlotsDocument,
    "\n    query booking($id: UUID!) {\n      booking(id: $id) {\n        id\n        conduct\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n    }\n  ": types.BookingDocument,
    "\n    query bookings($userPhone: String, $startsAfter: DateTime) {\n      bookings(userPhone: $userPhone, startsAfter: $startsAfter) {\n        id\n        conduct\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n    }\n  ": types.BookingsDocument,
    "\n    mutation deleteBooking($id: UUID!) {\n      deleteBooking(id: $id)\n    }\n  ": types.DeleteBookingDocument,
    "\n    mutation updateBooking(\n      $id: UUID!\n      $conduct: String!\n      $description: String!\n      $pocName: String!\n      $pocPhone: String!\n    ) {\n\n      updateBooking(\n        id: $id\n        conduct: $conduct\n        description: $description\n        pocName: $pocName\n        pocPhone: $pocPhone\n      ) {\n        id\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n\n    }\n  ": types.UpdateBookingDocument,
    "\n    mutation insertBooking(\n      $conduct: String!\n      $description: String!\n      $facilityName: String!\n      $pocName: String!\n      $pocPhone: String!\n      $startDateTime: DateTime!\n      $endDateTime: DateTime!\n    ) {\n\n      insertBooking(\n        conduct: $conduct\n        description: $description\n        facilityName: $facilityName\n        pocName: $pocName\n        pocPhone: $pocPhone\n        startDateTime: $startDateTime\n        endDateTime: $endDateTime\n      ) {\n        id\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n\n    }\n  ": types.InsertBookingDocument,
};

/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 *
 *
 * @example
 * ```ts
 * const query = graphql(`query GetUser($id: ID!) { user(id: $id) { name } }`);
 * ```
 *
 * The query argument is unknown!
 * Please regenerate the types.
 */
export function graphql(source: string): unknown;

/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n    query me {\n      me {\n        phone\n      }\n    }\n  "): (typeof documents)["\n    query me {\n      me {\n        phone\n      }\n    }\n  "];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n    query meWithName {\n      me {\n        name\n        phone\n      }\n    }\n  "): (typeof documents)["\n    query meWithName {\n      me {\n        name\n        phone\n      }\n    }\n  "];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n    query meWithEverything {\n      me {\n        id\n        unit\n        name\n        phone\n        telegramChatId\n        row\n      }\n    }\n  "): (typeof documents)["\n    query meWithEverything {\n      me {\n        id\n        unit\n        name\n        phone\n        telegramChatId\n        row\n      }\n    }\n  "];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n    query facilities {\n      facilities {\n        name\n      }\n    }\n  "): (typeof documents)["\n    query facilities {\n      facilities {\n        name\n      }\n    }\n  "];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n    query slots($facility: String!, $startDate: DateTime!, $endDate: DateTime!) {\n      facilities(name: $facility) {\n        name\n        availableTimeSlots(start: $startDate, end: $endDate) {\n          startDateTime\n          endDateTime\n          booking {\n            conduct\n            description\n            pocName\n            pocPhone\n            user {\n              unit\n            }\n          }\n        }\n      }\n    }\n  "): (typeof documents)["\n    query slots($facility: String!, $startDate: DateTime!, $endDate: DateTime!) {\n      facilities(name: $facility) {\n        name\n        availableTimeSlots(start: $startDate, end: $endDate) {\n          startDateTime\n          endDateTime\n          booking {\n            conduct\n            description\n            pocName\n            pocPhone\n            user {\n              unit\n            }\n          }\n        }\n      }\n    }\n  "];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n    query booking($id: UUID!) {\n      booking(id: $id) {\n        id\n        conduct\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n    }\n  "): (typeof documents)["\n    query booking($id: UUID!) {\n      booking(id: $id) {\n        id\n        conduct\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n    }\n  "];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n    query bookings($userPhone: String, $startsAfter: DateTime) {\n      bookings(userPhone: $userPhone, startsAfter: $startsAfter) {\n        id\n        conduct\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n    }\n  "): (typeof documents)["\n    query bookings($userPhone: String, $startsAfter: DateTime) {\n      bookings(userPhone: $userPhone, startsAfter: $startsAfter) {\n        id\n        conduct\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n    }\n  "];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n    mutation deleteBooking($id: UUID!) {\n      deleteBooking(id: $id)\n    }\n  "): (typeof documents)["\n    mutation deleteBooking($id: UUID!) {\n      deleteBooking(id: $id)\n    }\n  "];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n    mutation updateBooking(\n      $id: UUID!\n      $conduct: String!\n      $description: String!\n      $pocName: String!\n      $pocPhone: String!\n    ) {\n\n      updateBooking(\n        id: $id\n        conduct: $conduct\n        description: $description\n        pocName: $pocName\n        pocPhone: $pocPhone\n      ) {\n        id\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n\n    }\n  "): (typeof documents)["\n    mutation updateBooking(\n      $id: UUID!\n      $conduct: String!\n      $description: String!\n      $pocName: String!\n      $pocPhone: String!\n    ) {\n\n      updateBooking(\n        id: $id\n        conduct: $conduct\n        description: $description\n        pocName: $pocName\n        pocPhone: $pocPhone\n      ) {\n        id\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n\n    }\n  "];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n    mutation insertBooking(\n      $conduct: String!\n      $description: String!\n      $facilityName: String!\n      $pocName: String!\n      $pocPhone: String!\n      $startDateTime: DateTime!\n      $endDateTime: DateTime!\n    ) {\n\n      insertBooking(\n        conduct: $conduct\n        description: $description\n        facilityName: $facilityName\n        pocName: $pocName\n        pocPhone: $pocPhone\n        startDateTime: $startDateTime\n        endDateTime: $endDateTime\n      ) {\n        id\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n\n    }\n  "): (typeof documents)["\n    mutation insertBooking(\n      $conduct: String!\n      $description: String!\n      $facilityName: String!\n      $pocName: String!\n      $pocPhone: String!\n      $startDateTime: DateTime!\n      $endDateTime: DateTime!\n    ) {\n\n      insertBooking(\n        conduct: $conduct\n        description: $description\n        facilityName: $facilityName\n        pocName: $pocName\n        pocPhone: $pocPhone\n        startDateTime: $startDateTime\n        endDateTime: $endDateTime\n      ) {\n        id\n        description\n        startDateTime\n        endDateTime\n        facilityName\n        pocName\n        pocPhone\n      }\n\n    }\n  "];

export function graphql(source: string) {
  return (documents as any)[source] ?? {};
}

export type DocumentType<TDocumentNode extends DocumentNode<any, any>> = TDocumentNode extends DocumentNode<  infer TType,  any>  ? TType  : never;