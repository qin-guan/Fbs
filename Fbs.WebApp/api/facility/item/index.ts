/* tslint:disable */
/* eslint-disable */
// Generated by Microsoft Kiota
// @ts-ignore
import { TimeSlotsRequestBuilderRequestsMetadata, type TimeSlotsRequestBuilder } from './timeSlots/index.js';
// @ts-ignore
import { type BaseRequestBuilder, type KeysToExcludeForNavigationMetadata, type NavigationMetadata } from '@microsoft/kiota-abstractions';

/**
 * Builds and executes requests for operations under /Facility/{name}
 */
export interface WithNameItemRequestBuilder extends BaseRequestBuilder<WithNameItemRequestBuilder> {
    /**
     * The TimeSlots property
     */
    get timeSlots(): TimeSlotsRequestBuilder;
}
/**
 * Uri template for the request builder.
 */
export const WithNameItemRequestBuilderUriTemplate = "{+baseurl}/Facility/{name}";
/**
 * Metadata for all the navigation properties in the request builder.
 */
export const WithNameItemRequestBuilderNavigationMetadata: Record<Exclude<keyof WithNameItemRequestBuilder, KeysToExcludeForNavigationMetadata>, NavigationMetadata> = {
    timeSlots: {
        requestsMetadata: TimeSlotsRequestBuilderRequestsMetadata,
    },
};
/* tslint:enable */
/* eslint-enable */
