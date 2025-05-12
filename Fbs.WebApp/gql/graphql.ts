/* eslint-disable */
import type { TypedDocumentNode as DocumentNode } from '@graphql-typed-document-node/core';
export type Maybe<T> = T | null;
export type InputMaybe<T> = Maybe<T>;
export type Exact<T extends { [key: string]: unknown }> = { [K in keyof T]: T[K] };
export type MakeOptional<T, K extends keyof T> = Omit<T, K> & { [SubKey in K]?: Maybe<T[SubKey]> };
export type MakeMaybe<T, K extends keyof T> = Omit<T, K> & { [SubKey in K]: Maybe<T[SubKey]> };
export type MakeEmpty<T extends { [key: string]: unknown }, K extends keyof T> = { [_ in K]?: never };
export type Incremental<T> = T | { [P in keyof T]?: P extends ' $fragmentName' | '__typename' ? T[P] : never };
/** All built-in and custom scalars, mapped to their actual values */
export type Scalars = {
  ID: { input: string; output: string; }
  String: { input: string; output: string; }
  Boolean: { input: boolean; output: boolean; }
  Int: { input: number; output: number; }
  Float: { input: number; output: number; }
  /** The `DateTime` scalar represents an ISO-8601 compliant date time type. */
  DateTime: { input: any; output: any; }
  UUID: { input: any; output: any; }
};

/** Defines when a policy shall be executed. */
export enum ApplyPolicy {
  /** After the resolver was executed. */
  AfterResolver = 'AFTER_RESOLVER',
  /** Before the resolver was executed. */
  BeforeResolver = 'BEFORE_RESOLVER',
  /** The policy is applied in the validation step before the execution. */
  Validation = 'VALIDATION'
}

export type Booking = {
  __typename?: 'Booking';
  conduct?: Maybe<Scalars['String']['output']>;
  description?: Maybe<Scalars['String']['output']>;
  endDateTime?: Maybe<Scalars['DateTime']['output']>;
  facilityName?: Maybe<Scalars['String']['output']>;
  id?: Maybe<Scalars['String']['output']>;
  pocName?: Maybe<Scalars['String']['output']>;
  pocPhone?: Maybe<Scalars['String']['output']>;
  row: Scalars['Int']['output'];
  startDateTime?: Maybe<Scalars['DateTime']['output']>;
  userPhone?: Maybe<Scalars['String']['output']>;
};

export type BookingWithUser = {
  __typename?: 'BookingWithUser';
  conduct?: Maybe<Scalars['String']['output']>;
  description?: Maybe<Scalars['String']['output']>;
  endDateTime?: Maybe<Scalars['DateTime']['output']>;
  facilityName?: Maybe<Scalars['String']['output']>;
  id: Scalars['UUID']['output'];
  pocName?: Maybe<Scalars['String']['output']>;
  pocPhone?: Maybe<Scalars['String']['output']>;
  startDateTime?: Maybe<Scalars['DateTime']['output']>;
  user?: Maybe<User>;
};

export type Facility = {
  __typename?: 'Facility';
  availableTimeSlots: Array<TimeSlot>;
  group?: Maybe<Scalars['String']['output']>;
  id?: Maybe<Scalars['String']['output']>;
  name?: Maybe<Scalars['String']['output']>;
  row: Scalars['Int']['output'];
};


export type FacilityAvailableTimeSlotsArgs = {
  end: Scalars['DateTime']['input'];
  start: Scalars['DateTime']['input'];
};

export type Mutation = {
  __typename?: 'Mutation';
  deleteBooking: Scalars['UUID']['output'];
  insertBooking: Booking;
  updateBooking: Booking;
};


export type MutationDeleteBookingArgs = {
  id: Scalars['UUID']['input'];
};


export type MutationInsertBookingArgs = {
  conduct: Scalars['String']['input'];
  description: Scalars['String']['input'];
  endDateTime: Scalars['DateTime']['input'];
  facilityName: Scalars['String']['input'];
  pocName: Scalars['String']['input'];
  pocPhone: Scalars['String']['input'];
  startDateTime: Scalars['DateTime']['input'];
  userPhone?: InputMaybe<Scalars['String']['input']>;
};


export type MutationUpdateBookingArgs = {
  conduct: Scalars['String']['input'];
  description: Scalars['String']['input'];
  id: Scalars['UUID']['input'];
  pocName: Scalars['String']['input'];
  pocPhone: Scalars['String']['input'];
};

export type Query = {
  __typename?: 'Query';
  booking: BookingWithUser;
  bookings: Array<BookingWithUser>;
  facilities: Array<Facility>;
  me?: Maybe<User>;
};


export type QueryBookingArgs = {
  id: Scalars['UUID']['input'];
};


export type QueryBookingsArgs = {
  startsAfter?: InputMaybe<Scalars['DateTime']['input']>;
  userPhone?: InputMaybe<Scalars['String']['input']>;
};


export type QueryFacilitiesArgs = {
  name?: InputMaybe<Scalars['String']['input']>;
};

export type TimeSlot = {
  __typename?: 'TimeSlot';
  booking?: Maybe<BookingWithUser>;
  endDateTime: Scalars['DateTime']['output'];
  startDateTime: Scalars['DateTime']['output'];
};

export type User = {
  __typename?: 'User';
  id?: Maybe<Scalars['String']['output']>;
  name?: Maybe<Scalars['String']['output']>;
  notificationGroup?: Maybe<Scalars['String']['output']>;
  phone?: Maybe<Scalars['String']['output']>;
  row: Scalars['Int']['output'];
  telegramChatId?: Maybe<Scalars['String']['output']>;
  unit?: Maybe<Scalars['String']['output']>;
};

export type MeQueryVariables = Exact<{ [key: string]: never; }>;


export type MeQuery = { __typename?: 'Query', me?: { __typename?: 'User', phone?: string | null } | null };

export type MeWithNameQueryVariables = Exact<{ [key: string]: never; }>;


export type MeWithNameQuery = { __typename?: 'Query', me?: { __typename?: 'User', name?: string | null, phone?: string | null } | null };

export type MeWithEverythingQueryVariables = Exact<{ [key: string]: never; }>;


export type MeWithEverythingQuery = { __typename?: 'Query', me?: { __typename?: 'User', id?: string | null, unit?: string | null, name?: string | null, phone?: string | null, telegramChatId?: string | null, row: number } | null };

export type FacilitiesQueryVariables = Exact<{ [key: string]: never; }>;


export type FacilitiesQuery = { __typename?: 'Query', facilities: Array<{ __typename?: 'Facility', name?: string | null, group?: string | null }> };

export type AllSlotsQueryVariables = Exact<{
  startDate: Scalars['DateTime']['input'];
  endDate: Scalars['DateTime']['input'];
}>;


export type AllSlotsQuery = { __typename?: 'Query', facilities: Array<{ __typename?: 'Facility', name?: string | null, group?: string | null, availableTimeSlots: Array<{ __typename?: 'TimeSlot', startDateTime: any, endDateTime: any, booking?: { __typename?: 'BookingWithUser', id: any, conduct?: string | null, description?: string | null, pocName?: string | null, pocPhone?: string | null, user?: { __typename?: 'User', unit?: string | null } | null } | null }> }> };

export type SlotsQueryVariables = Exact<{
  facility: Scalars['String']['input'];
  startDate: Scalars['DateTime']['input'];
  endDate: Scalars['DateTime']['input'];
}>;


export type SlotsQuery = { __typename?: 'Query', facilities: Array<{ __typename?: 'Facility', name?: string | null, availableTimeSlots: Array<{ __typename?: 'TimeSlot', startDateTime: any, endDateTime: any, booking?: { __typename?: 'BookingWithUser', conduct?: string | null, description?: string | null, pocName?: string | null, pocPhone?: string | null, user?: { __typename?: 'User', unit?: string | null } | null } | null }> }> };

export type BookingQueryVariables = Exact<{
  id: Scalars['UUID']['input'];
}>;


export type BookingQuery = { __typename?: 'Query', booking: { __typename?: 'BookingWithUser', id: any, conduct?: string | null, description?: string | null, startDateTime?: any | null, endDateTime?: any | null, facilityName?: string | null, pocName?: string | null, pocPhone?: string | null } };

export type BookingsQueryVariables = Exact<{
  userPhone?: InputMaybe<Scalars['String']['input']>;
  startsAfter?: InputMaybe<Scalars['DateTime']['input']>;
}>;


export type BookingsQuery = { __typename?: 'Query', bookings: Array<{ __typename?: 'BookingWithUser', id: any, conduct?: string | null, description?: string | null, startDateTime?: any | null, endDateTime?: any | null, facilityName?: string | null, pocName?: string | null, pocPhone?: string | null }> };

export type DeleteBookingMutationVariables = Exact<{
  id: Scalars['UUID']['input'];
}>;


export type DeleteBookingMutation = { __typename?: 'Mutation', deleteBooking: any };

export type UpdateBookingMutationVariables = Exact<{
  id: Scalars['UUID']['input'];
  conduct: Scalars['String']['input'];
  description: Scalars['String']['input'];
  pocName: Scalars['String']['input'];
  pocPhone: Scalars['String']['input'];
}>;


export type UpdateBookingMutation = { __typename?: 'Mutation', updateBooking: { __typename?: 'Booking', id?: string | null, description?: string | null, startDateTime?: any | null, endDateTime?: any | null, facilityName?: string | null, pocName?: string | null, pocPhone?: string | null } };

export type InsertBookingMutationVariables = Exact<{
  conduct: Scalars['String']['input'];
  description: Scalars['String']['input'];
  facilityName: Scalars['String']['input'];
  pocName: Scalars['String']['input'];
  pocPhone: Scalars['String']['input'];
  startDateTime: Scalars['DateTime']['input'];
  endDateTime: Scalars['DateTime']['input'];
  userPhone?: InputMaybe<Scalars['String']['input']>;
}>;


export type InsertBookingMutation = { __typename?: 'Mutation', insertBooking: { __typename?: 'Booking', id?: string | null, description?: string | null, startDateTime?: any | null, endDateTime?: any | null, facilityName?: string | null, pocName?: string | null, pocPhone?: string | null } };


export const MeDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"me"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"me"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"phone"}}]}}]}}]} as unknown as DocumentNode<MeQuery, MeQueryVariables>;
export const MeWithNameDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"meWithName"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"me"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"phone"}}]}}]}}]} as unknown as DocumentNode<MeWithNameQuery, MeWithNameQueryVariables>;
export const MeWithEverythingDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"meWithEverything"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"me"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"unit"}},{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"phone"}},{"kind":"Field","name":{"kind":"Name","value":"telegramChatId"}},{"kind":"Field","name":{"kind":"Name","value":"row"}}]}}]}}]} as unknown as DocumentNode<MeWithEverythingQuery, MeWithEverythingQueryVariables>;
export const FacilitiesDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"facilities"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"facilities"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"group"}}]}}]}}]} as unknown as DocumentNode<FacilitiesQuery, FacilitiesQueryVariables>;
export const AllSlotsDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"allSlots"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"startDate"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"DateTime"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"endDate"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"DateTime"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"facilities"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"group"}},{"kind":"Field","name":{"kind":"Name","value":"availableTimeSlots"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"start"},"value":{"kind":"Variable","name":{"kind":"Name","value":"startDate"}}},{"kind":"Argument","name":{"kind":"Name","value":"end"},"value":{"kind":"Variable","name":{"kind":"Name","value":"endDate"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"startDateTime"}},{"kind":"Field","name":{"kind":"Name","value":"endDateTime"}},{"kind":"Field","name":{"kind":"Name","value":"booking"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"conduct"}},{"kind":"Field","name":{"kind":"Name","value":"description"}},{"kind":"Field","name":{"kind":"Name","value":"pocName"}},{"kind":"Field","name":{"kind":"Name","value":"pocPhone"}},{"kind":"Field","name":{"kind":"Name","value":"user"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"unit"}}]}}]}}]}}]}}]}}]} as unknown as DocumentNode<AllSlotsQuery, AllSlotsQueryVariables>;
export const SlotsDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"slots"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"facility"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"startDate"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"DateTime"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"endDate"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"DateTime"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"facilities"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"name"},"value":{"kind":"Variable","name":{"kind":"Name","value":"facility"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"availableTimeSlots"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"start"},"value":{"kind":"Variable","name":{"kind":"Name","value":"startDate"}}},{"kind":"Argument","name":{"kind":"Name","value":"end"},"value":{"kind":"Variable","name":{"kind":"Name","value":"endDate"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"startDateTime"}},{"kind":"Field","name":{"kind":"Name","value":"endDateTime"}},{"kind":"Field","name":{"kind":"Name","value":"booking"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"conduct"}},{"kind":"Field","name":{"kind":"Name","value":"description"}},{"kind":"Field","name":{"kind":"Name","value":"pocName"}},{"kind":"Field","name":{"kind":"Name","value":"pocPhone"}},{"kind":"Field","name":{"kind":"Name","value":"user"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"unit"}}]}}]}}]}}]}}]}}]} as unknown as DocumentNode<SlotsQuery, SlotsQueryVariables>;
export const BookingDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"booking"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"booking"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"conduct"}},{"kind":"Field","name":{"kind":"Name","value":"description"}},{"kind":"Field","name":{"kind":"Name","value":"startDateTime"}},{"kind":"Field","name":{"kind":"Name","value":"endDateTime"}},{"kind":"Field","name":{"kind":"Name","value":"facilityName"}},{"kind":"Field","name":{"kind":"Name","value":"pocName"}},{"kind":"Field","name":{"kind":"Name","value":"pocPhone"}}]}}]}}]} as unknown as DocumentNode<BookingQuery, BookingQueryVariables>;
export const BookingsDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"bookings"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"userPhone"}},"type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"startsAfter"}},"type":{"kind":"NamedType","name":{"kind":"Name","value":"DateTime"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"bookings"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"userPhone"},"value":{"kind":"Variable","name":{"kind":"Name","value":"userPhone"}}},{"kind":"Argument","name":{"kind":"Name","value":"startsAfter"},"value":{"kind":"Variable","name":{"kind":"Name","value":"startsAfter"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"conduct"}},{"kind":"Field","name":{"kind":"Name","value":"description"}},{"kind":"Field","name":{"kind":"Name","value":"startDateTime"}},{"kind":"Field","name":{"kind":"Name","value":"endDateTime"}},{"kind":"Field","name":{"kind":"Name","value":"facilityName"}},{"kind":"Field","name":{"kind":"Name","value":"pocName"}},{"kind":"Field","name":{"kind":"Name","value":"pocPhone"}}]}}]}}]} as unknown as DocumentNode<BookingsQuery, BookingsQueryVariables>;
export const DeleteBookingDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"deleteBooking"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"deleteBooking"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}]}]}}]} as unknown as DocumentNode<DeleteBookingMutation, DeleteBookingMutationVariables>;
export const UpdateBookingDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"updateBooking"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"conduct"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"description"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"pocName"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"pocPhone"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"updateBooking"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}},{"kind":"Argument","name":{"kind":"Name","value":"conduct"},"value":{"kind":"Variable","name":{"kind":"Name","value":"conduct"}}},{"kind":"Argument","name":{"kind":"Name","value":"description"},"value":{"kind":"Variable","name":{"kind":"Name","value":"description"}}},{"kind":"Argument","name":{"kind":"Name","value":"pocName"},"value":{"kind":"Variable","name":{"kind":"Name","value":"pocName"}}},{"kind":"Argument","name":{"kind":"Name","value":"pocPhone"},"value":{"kind":"Variable","name":{"kind":"Name","value":"pocPhone"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"description"}},{"kind":"Field","name":{"kind":"Name","value":"startDateTime"}},{"kind":"Field","name":{"kind":"Name","value":"endDateTime"}},{"kind":"Field","name":{"kind":"Name","value":"facilityName"}},{"kind":"Field","name":{"kind":"Name","value":"pocName"}},{"kind":"Field","name":{"kind":"Name","value":"pocPhone"}}]}}]}}]} as unknown as DocumentNode<UpdateBookingMutation, UpdateBookingMutationVariables>;
export const InsertBookingDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"insertBooking"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"conduct"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"description"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"facilityName"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"pocName"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"pocPhone"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"startDateTime"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"DateTime"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"endDateTime"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"DateTime"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"userPhone"}},"type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"insertBooking"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"conduct"},"value":{"kind":"Variable","name":{"kind":"Name","value":"conduct"}}},{"kind":"Argument","name":{"kind":"Name","value":"description"},"value":{"kind":"Variable","name":{"kind":"Name","value":"description"}}},{"kind":"Argument","name":{"kind":"Name","value":"facilityName"},"value":{"kind":"Variable","name":{"kind":"Name","value":"facilityName"}}},{"kind":"Argument","name":{"kind":"Name","value":"pocName"},"value":{"kind":"Variable","name":{"kind":"Name","value":"pocName"}}},{"kind":"Argument","name":{"kind":"Name","value":"pocPhone"},"value":{"kind":"Variable","name":{"kind":"Name","value":"pocPhone"}}},{"kind":"Argument","name":{"kind":"Name","value":"startDateTime"},"value":{"kind":"Variable","name":{"kind":"Name","value":"startDateTime"}}},{"kind":"Argument","name":{"kind":"Name","value":"endDateTime"},"value":{"kind":"Variable","name":{"kind":"Name","value":"endDateTime"}}},{"kind":"Argument","name":{"kind":"Name","value":"userPhone"},"value":{"kind":"Variable","name":{"kind":"Name","value":"userPhone"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"description"}},{"kind":"Field","name":{"kind":"Name","value":"startDateTime"}},{"kind":"Field","name":{"kind":"Name","value":"endDateTime"}},{"kind":"Field","name":{"kind":"Name","value":"facilityName"}},{"kind":"Field","name":{"kind":"Name","value":"pocName"}},{"kind":"Field","name":{"kind":"Name","value":"pocPhone"}}]}}]}}]} as unknown as DocumentNode<InsertBookingMutation, InsertBookingMutationVariables>;