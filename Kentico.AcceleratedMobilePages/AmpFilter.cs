﻿using System;
using System.Linq;
using System.Text.RegularExpressions;

using CMS.DataEngine;
using CMS.DocumentEngine;
using CMS.Helpers;
using CMS.MacroEngine;
using CMS.OutputFilter;
using CMS.PortalEngine;
using CMS.SiteProvider;

using HtmlAgilityPack;

namespace Kentico.AcceleratedMobilePages
{
    public class AmpFilter
    {
        /// <summary>
        /// String for importing custom elements in head tag
        /// </summary>
        private string customElementsScripts;


        /// <summary>
        /// Returns URL protocol prefix depending on the current connection
        /// </summary>
        private string ConnectionProtocolPrefix => CMSHttpContext.Current.Request.IsSecureConnection ? Constants.P_HTTPS : Constants.P_HTTP;


        /// <summary>
        /// If the filter is enabled for current page, final HTML will be modified
        /// </summary>
        /// <param name="filter">Output filter</param>
        /// <param name="finalHtml">Final HTML string</param>
        public void OnFilterActivated(ResponseOutputFilter filter, ref string finalHtml)
        {
            int state = CheckStateHelper.GetFilterState();
            if (state == Constants.ENABLED_AND_ACTIVE)
            {
                finalHtml = TransformToAmpHtml(finalHtml);
            }
            else if (state == Constants.ENABLED_AND_INACTIVE)
            {
                finalHtml = AppendAmpHtmlLink(finalHtml);
            }
        }


        /// <summary>
        /// Returns modified HTML in AMP HTML markup
        /// </summary>
        /// <param name="finalHtml">Final HTML string</param>
        private string TransformToAmpHtml(string finalHtml)
        {
            customElementsScripts = "";

            // Process the resulting HTML using HTML Agility Pack parser
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(finalHtml);
            RemoveRestrictedElements(doc);
            ResolveComplexElements(doc);
            ReplaceRegularTagsByAmpTags(doc);

            // Do the rest using regular expressions
            finalHtml = InsertCompulsoryMarkupAndCss(doc.DocumentNode.InnerHtml);
            finalHtml = PerformRegexCorrections(finalHtml);

            return finalHtml;
        }


        /// <summary>
        /// Returns final HTML with appended link pointing to AMP version of current page
        /// </summary>
        /// <param name="finalHtml">Final HTML string</param>
        private string AppendAmpHtmlLink(string finalHtml)
        {
            string ampLink = ConnectionProtocolPrefix +
                             Settings.AmpFilterDomainAlias +
                             GetDocumentPath();
            string metaTag = String.Format(Constants.AMP_AMP_HTML_LINK, ampLink) + Constants.NEW_LINE;
            // Insert meta tag
            finalHtml = finalHtml.Replace("</head>", metaTag + "</head>");

            return finalHtml;
        }


        /// <summary>
        /// Removes elements restricted by AMP HTML standard
        /// </summary>
        /// <param name="doc">The complete HtmlDocument</param>
        private void RemoveRestrictedElements(HtmlDocument doc)
        {
            // Remove restricted elements
            foreach (var rule in Constants.XPATH_RESTRICTED_ELEMENTS)
            {
                RemoveElement(doc, rule);
            }

            // Remove attributes
            RemoveAttribute(doc, Constants.XPATH_ATTR_STYLE, Constants.XPATH_ATTR_STYLE_NAME);
        }


        /// <summary>
        /// Performs corrections required by AMP HTML standard using Regular Expressions
        /// These corrections were not possible using HTML Agility Pack parser
        /// </summary>
        /// <param name="finalHtml">Final HTML string</param>
        private string PerformRegexCorrections(string finalHtml)
        {
            // Initial HTML amp tag
            finalHtml = finalHtml.Replace(Constants.HTML_TAG, Constants.HTML_REPLACEMENT);

            // Remove conditional comments
            finalHtml = new Regex(Constants.REGEX_CONDITIONAL_COMMENTS).Replace(finalHtml, "");

            // Remove restricted attributes
            finalHtml = new Regex(Constants.REGEX_ATTR_ONANY_SUFFIX).Replace(finalHtml, "");
            finalHtml = new Regex(Constants.REGEX_ATTR_XML_ATTRIBUTES).Replace(finalHtml, "");
            finalHtml = new Regex(Constants.REGEX_ATTR_IAMPANY_SUFFIX).Replace(finalHtml, "");

            // Remove restricted attribute's values
            finalHtml = new Regex(Constants.REGEX_NAME_CLASS).Replace(finalHtml, "");
            finalHtml = new Regex(Constants.REGEX_NAME_ID).Replace(finalHtml, "");

            return finalHtml;
        }

        /// <summary>
        /// Resolves more complex restrictions put on specific elements using HTML parser
        /// </summary>
        /// <param name="doc">The complete HtmlDocument</param>
        private void ResolveComplexElements(HtmlDocument doc)
        {
            // Script tags source URLs from settings
            string ampCustomForm = String.Format(Constants.AMP_CUSTOM_ELEMENT_AMP_FORM, Settings.AmpFilterFormScriptUrl);

            // Process <form> tags
            HtmlNodeCollection nodes = doc.DocumentNode.SelectNodes(Constants.XPATH_FORM);
            if (nodes != null)
            {
                foreach (HtmlNode node in nodes)
                {
                    // If attribute method="post" action attribute must be replaced by action-xhr
                    if ((node.Attributes["method"] != null) && (node.Attributes["method"].Value.ToLower() == "post"))
                    {
                        if (node.Attributes["action"] != null)
                        {
                            node.Attributes["action"].Name = "action-xhr";
                        }
                    }

                    // Ensure that target attribute has correct value or add target attribute with correct value
                    if ((node.Attributes["target"] == null) || (
                        (node.Attributes["target"] != null) && (node.Attributes["target"].Value.ToLower() != "_top")))
                    {
                        node.SetAttributeValue("target", "_blank");
                    }
                }

                // At least one <form> tag is used, we need to import custom element
                customElementsScripts += ampCustomForm + Constants.NEW_LINE;
            }

            // Process included fonts, only fonts from group of providers are allowed
            nodes = doc.DocumentNode.SelectNodes(Constants.XPATH_FONT_STYLESHEET);

            // List of font providers from settings
            var fontProviders = Settings.AmpFilterFontProviders;
            if (nodes != null)
            {
                foreach (HtmlNode node in nodes)
                {
                    var elementAllowed = false;
                    if (node.Attributes["href"] != null)
                    {
                        foreach (string provider in fontProviders)
                        {
                            if (node.Attributes["href"].Value.ToLower().StartsWith(provider.Trim()))
                            {
                                elementAllowed = true;
                                break;
                            }
                        }
                    }
                    if (!elementAllowed)
                    {
                        node.Remove();
                    }
                }
            }
        }


        /// <summary>
        /// Replaces standard HTML tags by special AMP HTML tags and appends import scripts for custom elements
        /// </summary>
        /// <param name="doc">The complete HtmlDocument</param>
        private void ReplaceRegularTagsByAmpTags(HtmlDocument doc)
        {
            ReplaceElement(doc, Constants.XPATH_IMG, Constants.XPATH_IMG_REPLACEMENT);
            if (ReplaceElement(doc, Constants.XPATH_VIDEO, Constants.XPATH_VIDEO_REPLACEMENT))
            {
                string ampCustomVideo = String.Format(Constants.AMP_CUSTOM_ELEMENT_AMP_VIDEO, Settings.AmpFilterVideoScriptUrl);
                customElementsScripts += ampCustomVideo + Constants.NEW_LINE;
            }
            if (ReplaceElement(doc, Constants.XPATH_AUDIO, Constants.XPATH_AUDIO_REPLACEMENT))
            {
                string ampCustomAudio = String.Format(Constants.AMP_CUSTOM_ELEMENT_AMP_AUDIO, Settings.AmpFilterAudioScriptUrl);
                customElementsScripts += ampCustomAudio + Constants.NEW_LINE;
            }
            if (ReplaceElement(doc, Constants.XPATH_IFRAME, Constants.XPATH_IFRAME_REPLACEMENT))
            {
                string ampCustomIframe = String.Format(Constants.AMP_CUSTOM_ELEMENT_AMP_IFRAME, Settings.AmpFilterIframeScriptUrl);
                customElementsScripts += ampCustomIframe + Constants.NEW_LINE;
            }
        }


        /// <summary>
        /// Inserts compulsory AMP markup and inline CSS
        /// </summary>
        /// <param name="finalHtml">Final HTML string</param>
        private string InsertCompulsoryMarkupAndCss(string finalHtml)
        {
            // Save the original <head> tag before replacement later
            String headTag = "";
            Match m = Constants.HeadRegex.Match(finalHtml);
            if (m.Success)
            {
                headTag = m.Value;
            }

            // Script tags source URLs from settings
            string ampRuntimeScript = String.Format(Constants.AMP_SCRIPT, Settings.AmpFilterRuntimeScriptUrl);

            // CSS stylesheet to be inlined to finalHtml
            string styles = GetStylesheetText();

            // Create a link pointing to the regular HTML version of the page
            string canonicalLink = ConnectionProtocolPrefix +
                                   SiteContext.CurrentSite.DomainName +
                                   GetDocumentPath();

            // Extend the <head> tag with the compulsory markup and CSS styles
            headTag += Constants.NEW_LINE +
                        Constants.AMP_CHARSET + Constants.NEW_LINE +
                        ampRuntimeScript + Constants.NEW_LINE +
                        customElementsScripts +
                        String.Format(Constants.AMP_CANONICAL_HTML_LINK, canonicalLink) + Constants.NEW_LINE +
                        Constants.AMP_VIEWPORT + Constants.NEW_LINE +
                        Constants.AMP_BOILERPLATE_CODE + Constants.NEW_LINE +
                        String.Format(Constants.AMP_CUSTOM_STYLE, styles) + Constants.NEW_LINE;

            return Constants.HeadRegex.Replace(finalHtml, headTag);
        }


        /// <summary>
        /// Returns CSS stylesheet for current page.
        /// Stylesheet can be:
        ///     - normal CSS of current page
        ///     - default CSS for all AMP pages
        ///     - CSS set as AMP stylesheet for current page
        /// </summary>
        private string GetStylesheetText()
        {
            string cssText = "";

            // Checking which CSS file to use
            ObjectQuery<AmpFilterInfo> q = AmpFilterInfoProvider.GetAmpFilters().WhereEquals("PageNodeGuid", DocumentContext.CurrentPageInfo.NodeGUID.ToString());
            AmpFilterInfo ampFilterInfo = q.FirstOrDefault();

            bool useDefaultStylesheet = ampFilterInfo?.UseDefaultStylesheet ?? true;
            if (useDefaultStylesheet)
            {
                // Get the ID of default AMP CSS
                string defaultID = Settings.AmpFilterDefaultCSS;
                var cssID = ValidationHelper.GetInteger(defaultID, 0);

                // Default AMP CSS is not set, using ordinary CSS of current page
                if (cssID == 0)
                {
                    cssText = DocumentContext.CurrentDocumentStylesheet?.StylesheetText;
                }
                else
                {
                    // Use default AMP CSS stylesheet
                    var cssInfo = CssStylesheetInfoProvider.GetCssStylesheetInfo(cssID);
                    if (cssInfo != null)
                    {
                        cssText = cssInfo.StylesheetText;
                    }
                }
            }
            else
            {
                // Use specific AMP CSS set for this page
                int stylesheetID = ampFilterInfo?.StylesheetID ?? 0;
                var cssInfo = CssStylesheetInfoProvider.GetCssStylesheetInfo(stylesheetID);
                if (cssInfo != null)
                {
                    cssText = cssInfo.StylesheetText;
                }
            }

            // Resolve macros
            cssText = MacroResolver.Resolve(cssText);

            // Resolve client URL
            return HTMLHelper.ResolveCSSClientUrls(cssText, CMSHttpContext.Current.Request.Url.ToString());
        }


        /// <summary>
        /// Removes element using HTML parser.
        /// </summary>
        /// <param name="doc">The complete HtmlDocument</param>
        /// <param name="elementXPath">XPath specifying the element</param>
        private void RemoveElement(HtmlDocument doc, string elementXPath)
        {
            HtmlNodeCollection nodes = doc.DocumentNode.SelectNodes(elementXPath);
            if (nodes != null)
            {
                foreach (HtmlNode node in nodes)
                {
                    node.Remove();
                }
            }
        }


        /// <summary>
        /// Replaces element using HTML parser and return true if at least one node was replaced.
        /// </summary>
        /// <param name="doc">The complete HtmlDocument</param>
        /// <param name="xPath">XPath specifying the element</param>
        /// <param name="replacement">New name of the element</param>
        private bool ReplaceElement(HtmlDocument doc, string xPath, string replacement)
        {
            var nodes = doc.DocumentNode.SelectNodes(xPath);
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    node.Name = replacement;
                }
            }
            return (nodes != null);
        }


        /// <summary>
        /// Replaces element's attribute using HTML parser.
        /// </summary>
        /// <param name="doc">The complete HtmlDocument</param>
        /// <param name="xPath">XPath specifying the element</param>
        /// <param name="attrName">Name of the attribute</param>
        private void RemoveAttribute(HtmlDocument doc, string xPath, string attrName)
        {
            var nodes = doc.DocumentNode.SelectNodes(xPath);
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    node.Attributes.Remove(attrName);
                }
            }
        }


        /// <summary>
        /// Returns path of the currently processed document
        /// </summary>
        private string GetDocumentPath()
        {
            var documentPath = RequestContext.CurrentRelativePath;

            return documentPath + Settings.CmsFriendlyUrlExtension;
        }
    }
}
