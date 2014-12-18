// OpenCVSharp for Unity CAMShift Tracking
//  Attach this to a Unity Game Object and that object will display 
//  the webcam image while tracking a region of interest defined by
//  the user when they select a box with the mouse.
//
// Tony Reina
// Created: 30 October 2014
//
// $Id: VideoCaptureScript.cs 41 2014-12-18 03:23:44Z tbreina $
//
// This file may be used under the terms of the 2-clause BSD license:
//
// Copyright (c) 2014, G. Anthony Reina
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are
// permitted provided that the following conditions are met:
//
//    * Redistributions of source code must retain the above copyright notice, this list
//      of conditions and the following disclaimer.
//    * Redistributions in binary form must reproduce the above copyright notice, this
//      list of conditions and the following disclaimer in the documentation and/or other
//      materials provided with the distribution.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY
// EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
// OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT
// SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
// INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
// PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
// STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF
// THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using OpenCvSharp;   // OpenCVSharp 2.4.8


// Parallel is used to speed up the for loop when converting IplImage to 2D texture
using Uk.Org.Adcock.Parallel;  // Stewart Adcock's implementation of parallel processing 


public class VideoCaptureScript : MonoBehaviour
{

		public GameObject marker; // External game object which can track the ROI

		// Flip the video source axes (webcams are usually mirrored)
		// Unity and OpenCV images are flipped
		public bool FlipUpDownAxis = false, FlipLeftRightAxis = true;

		// Displays the region of interest chosen by mouse 
		public bool DisplayROIFlag = true;

		// Object parameters - rectangle where the attached gameObject is in screen coordinates
		Rect objectScreenPosition = new Rect (0, 0, 1, 1);

		// Video parameters
		//////////////////////////////////
		private WebCamTexture _webcamTexture;     //Texture retrieved from the webcam
		private string deviceName;  // input video devicename
		private int imWidth;  // Input devices image width
		private int imHeight;  // Input devices image height 
		private int imColorChannels = 3; // Number of color channels (red, blue, green (or HSV))
		private BitDepth imBitDepth = BitDepth.U8; // Unsigned 8-bit value for color channels

		// Image from the webcam (or video source)
		// If you don't declare this as "static", then you'll get the runtime error:
		// The same field name is serialized multiple times in the class or it's parent class. 
		// This is not supported:  -> Base(VideoCaptureScript) inputImage(IplImage) disposed
		static IplImage videoSourceImage;                 //Ipl image of the converted video texture

		// Select region of interest (ROI)
		//////////////////////////////////

		// Select box via the mouse
		// Allows user to select a sub-region (aka Region Of Interest) from the source video 
		bool _mouseIsDown = false;   // Mouse button is down
		Vector2 _mouseDownPos = Vector2.zero;  // Mouse position when mouse button clicked
		Vector2 _mouseLastPos = Vector2.zero;  // Current mouse position

		// For CamShift
		CvHistogram _histogramToTrack;  // Histogram in the region of interest we want to keep
		CvRect _rectToTrack;  // The rectangle defining the region of interest (ROI) to track
		CvBox2D rotatedBoxToTrack;
		bool trackFlag = false;  // If true, then track the ROI with CamShift

		// For the threshold window
		// HSV Theshold Range Slider values
		int _hueLow = 10;
		int _hueHigh = 179;
		int _satLow = 10;
		int _satHigh = 255;

		// Display flags
		bool backprojWindowFlag = false;
		bool histoWindowFlag = false;
		bool trackWindowFlag = false;

		// Kalman filter
		// Create the Kalman Filter
		CvKalman _kalman;
		CvPoint lastPosition;

		// Use this for initialization of the class
		void Start ()
		{

				//Webcam initialisation
				WebCamDevice[] devices = WebCamTexture.devices;
				//Debug.Log ("Number of video devices = " + devices.Length);
		
				if (devices.Length > 0) {   // If there is at least one camera

						_webcamTexture = new WebCamTexture (devices [0].name);  // Grab first camera
						//Debug.Log ("Device name = " + devices [0].name);

						// Attach camera to texture of the gameObject
						renderer.material.mainTexture = _webcamTexture; 

						// Un-mirror the webcam image
						if (FlipLeftRightAxis) {
								transform.localScale = new Vector3 (-transform.localScale.x,
			                                   transform.localScale.y, transform.localScale.z);
						}

						if (FlipUpDownAxis) {
								transform.localScale = new Vector3 (transform.localScale.x,
				                                    -transform.localScale.y, transform.localScale.z);
						}


						_webcamTexture.Play ();  // Play the video source

						// Get the vieo source image width and height
						imWidth = _webcamTexture.width;
						imHeight = _webcamTexture.height;
						
						// Create standard IplImage based on web camera video input
						// 3 channels for color images with unsigned 8-bit depth of color values
						videoSourceImage = new IplImage (imWidth, imHeight, imBitDepth, imColorChannels);

						CreateOutputWindows (); // Create the openCV windows if desired;

				}
		
		}

		// Create some output windows to show the intermediate OpenCV steps
		// e.g. Thresholding, BackProjections, Histograms
		void CreateOutputWindows ()
		{

				// OpenCV windows only needed to be explicitly created here at 
				// startup if we are mofying the window in some way. For example,
				// we add HSV range slider bars to the thresholding window.
//				Cv.NamedWindow ("HSV Thresholded Image");
//				Cv.CreateTrackbar ("Hue Low", "HSV Thresholded Image", _hueLow, 179, onTrackbarHueLow);
//				Cv.CreateTrackbar ("Hue High", "HSV Thresholded Image", _hueHigh, 179, onTrackbarHueHigh);
//				Cv.CreateTrackbar ("Sat Low", "HSV Thresholded Image", _satLow, 255, onTrackbarSatLow);
//				Cv.CreateTrackbar ("Sat High", "HSV Thresholded Image", _satHigh, 255, onTrackbarSatHigh);

		}


		// Find the attached GameObject's position in screen space
		void FindObjectScreenPosition ()
		{
				// Update where the object's top left corner is in screen coordinates
				Vector3 offset = Vector3.zero;
				// Make offset the top-left corner of the gameObject
				offset.x = -Mathf.Abs (transform.localScale.x / 2.0f);  // Half of the x scale
				offset.y = Mathf.Abs (transform.localScale.y / 2.0f); // Half of the y scale
				// Convert world position to screen (pixel) position
				// transform.position is the exact center of the gameObject
				Vector3 objectTopLeftScreen = Camera.main.WorldToScreenPoint (transform.position + offset);
				// Screen y axis is flipped of world y axis.
				objectTopLeftScreen.y = Screen.height - objectTopLeftScreen.y;

				Vector3 objectBottomRightScreen = Camera.main.WorldToScreenPoint (transform.position - offset);
				objectBottomRightScreen.y = Screen.height - objectBottomRightScreen.y;

				objectScreenPosition.Set (objectTopLeftScreen.x, objectTopLeftScreen.y,
		                          Mathf.Abs (objectTopLeftScreen.x - objectBottomRightScreen.x),
		                          Mathf.Abs (objectTopLeftScreen.y - objectBottomRightScreen.y));

		}

		// Converts the ROI (screen coordinates) into world coordinates and
		// positions world gameobject to that position and rotation
		void ROIScreenToGameObject (CvBox2D boxToTrack, GameObject obj1)
		{

				Vector2 origin;
				origin.x = objectScreenPosition.position.x + scaleObjectWidth (boxToTrack.Center.X);
				origin.y = objectScreenPosition.position.y + scaleObjectHeight (boxToTrack.Center.Y);

				obj1.transform.position = Camera.main.ScreenToWorldPoint (new Vector3 (origin.x, Screen.height - origin.y, 
		                                                         Mathf.Abs (transform.position.z - 
						Camera.main.transform.position.z)));
				

				obj1.transform.eulerAngles = new Vector3 (0, 0, 90 - boxToTrack.Angle);
				obj1.transform.localScale = new Vector3 (scaleObjectHeight (boxToTrack.Size.Height) / 100, 
		                                           scaleObjectWidth (boxToTrack.Size.Width) / 100, 1);

		}
	
		// Update and OnGUI are the main loops
		void Update ()
		{

				FindObjectScreenPosition ();
				
				if (_webcamTexture.isPlaying) {
			
						if (_webcamTexture.didUpdateThisFrame) {
								//convert Unity 2D texture from webcam to IplImage
								Texture2DToIplImage ();

								// Do some image processing with OpenCVSharp on this image frame
								ProcessImage (videoSourceImage);
						}
			
				} else {
						Debug.Log ("Can't find camera!");
				}

				if (Input.GetKeyDown (KeyCode.H))  // "h" key turns histogram screen on/off
						histoWindowFlag = !histoWindowFlag;
		

				if (trackFlag) {
						if (Input.GetKeyDown (KeyCode.B))  // "b" key turns back projection on/off
								backprojWindowFlag = !backprojWindowFlag;
						if (Input.GetKeyDown (KeyCode.T))  // "t" key turns tracking openCV window on
								trackWindowFlag = !trackWindowFlag;

						// Move an external game object based on the ROI being tracked
						if (marker)
								ROIScreenToGameObject (rotatedBoxToTrack, marker);

				}
		    
				if (Input.GetMouseButtonDown (1)) { // Right mouse button 
						Debug.Log ("Tracking off");
						trackFlag = false;
						_mouseIsDown = false;
				} else if (Input.GetMouseButtonDown (0)) {  // Left mouse button
			
						if (!_mouseIsDown) {
								_mouseDownPos = Input.mousePosition;
								trackFlag = false;
								
						}

						_mouseIsDown = true;
				}


				if (Input.GetMouseButtonUp (0)) {  // Left mouse button is up

						// If mouse went from down to up, then update the region of interest using the box
						if (_mouseIsDown) {
				
								// Calculate the histogram for the selected region of interest (ROI)
								_rectToTrack = CheckROIBounds (ConvertRect2CvRect (MakePixelBox (_mouseDownPos, _mouseLastPos)));

								if (DisplayROIFlag) {
										// Draw the region of interest to track
										DrawROIBox (videoSourceImage);
								}

								// Use Hue/Saturation histogram (not just the Hue dimension)
								_histogramToTrack = CalculateHSVHistogram (GetROI (videoSourceImage, _rectToTrack));
				
								// Use Hue channel histogram only
								//_histogramToTrack = CalculateOneChannelHistogram (GetROI (videoSourceImage, _rectToTrack), 0, 179);			
				
								lastPosition = new CvPoint (Mathf.FloorToInt (_rectToTrack.X), Mathf.FloorToInt (_rectToTrack.Y));
								InitializeKalmanFilter ();

								

								trackFlag = true;
						}
			
						_mouseIsDown = false;

				}
		
		}

		void ProcessImage (IplImage _image)
		{

				// IplImage structure
				//		typedef struct _IplImage {
				//			int                  nSize;
				//			int                  ID;
				//			int                  nChannels;
				//			int                  alphaChannel;
				//			int                  depth;
				//			char                 colorModel[4];
				//			char                 channelSeq[4];
				//			int                  dataOrder;
				//			int                  origin;
				//			int                  align;
				//			int                  width;
				//			int                  height;
				//			struct _IplROI*      roi;
				//			struct _IplImage*    maskROI;
				//			void*                imageId;
				//			struct _IplTileInfo* tileInfo;
				//			int                  imageSize;
				//			char*                imageData;
				//			int                  widthStep;
				//			int                  BorderMode[4];
				//			int                  BorderConst[4];
				//			char*                imageDataOrigin;
				//		} IplImage;

				// TODO: I think it's preferred to use CvMat rather than IplImage
				if (trackFlag) {
						
						CalculateCamShift (_image);
						
				}

				if (histoWindowFlag) {
						Draw1DHistogram (_image);
				}
				//	DrawThresholdImage (_image);

		}
	

		// Creates an image from a 2D Histogram (x axis = Hue, y axis = Saturation)
		void DrawHSHistogram (CvHistogram hist)
		{

				// Get the maximum and minimum values from the histogram
				float minValue, maxValue;
				hist.GetMinMaxValue (out minValue, out maxValue);

				int xBins = hist.Bins.GetDimSize (0);  // Number of hue bins (x axis)
				int yBins = hist.Bins.GetDimSize (1);  // Number of saturation bins (y axis)

				// Create an image to visualize the histogram
				int scaleHeight = 5, scaleWidth = 5;
				IplImage hist_img = new IplImage (Cv.Size (xBins * scaleWidth, yBins * scaleHeight), imBitDepth, imColorChannels);
				hist_img.Zero (); // Set all the pixels to black 

				double binVal;
				int _intensity;
				for (int h = 0; h < xBins; h++) {
						for (int s = 0; s < yBins; s++) {

								binVal = Cv.QueryHistValue_2D (hist, h, s);
								_intensity = Cv.Round (binVal / maxValue * 255); // 0 to 255

								// Draw a rectangle (h, s) to (h+1, s+1)  (scaled by window size)
								//  The pixel value is the color of the histogram value at bin (h, s)
								hist_img.Rectangle (Cv.Point (h * scaleWidth, s * scaleHeight),
				                    Cv.Point ((h + 1) * scaleWidth - 1, (s + 1) * scaleHeight - 1),
				                    Cv.RGB (_intensity, _intensity, _intensity),
				                    Cv.FILLED);
						}
				}

		
				Cv.ShowImage ("HS Histogram", hist_img);

				Cv.ReleaseImage (hist_img);
		
		}

		// Creates an image from a 1D Histogram
		void Draw1DHistogram (IplImage _image)
		{

				float channelMax = 255;

				CvHistogram hist1 = CalculateOneChannelHistogram (_image, 0, channelMax);
				CvHistogram hist2 = CalculateOneChannelHistogram (_image, 1, channelMax);
				CvHistogram hist3 = CalculateOneChannelHistogram (_image, 2, channelMax);

	
				// Get the maximum and minimum values from the histogram
				float minValue, maxValue;
				hist1.GetMinMaxValue (out minValue, out maxValue);
		
				int hBins = hist1.Bins.GetDimSize (0);  // Number of bins


				// Create an image to visualize the histogram
				int scaleWidth = 3, scaleHeight = 1;
				int histWidth = hBins * imColorChannels * scaleWidth, histHeight = Mathf.FloorToInt (channelMax * scaleHeight);
				IplImage hist_img = new IplImage (Cv.Size (histWidth, histHeight), 
		                                  imBitDepth, imColorChannels);
				hist_img.Zero (); // Set all the pixels to black 
		
				double binVal;
				int _intensity;
				for (int h = 0; h < hBins; h++) {
				
				
						// Draw Channel 1
						binVal = Cv.QueryHistValue_1D (hist1, h);
						_intensity = Cv.Round (binVal / maxValue * channelMax) * scaleHeight; // 0 to channelMax
				
						// Draw a rectangle (h, s) to (h+1, s+1)  (scaled by window size)
						//  The pixel value is the color of the histogram value at bin (h, s)
						hist_img.Rectangle (Cv.Point (h * imColorChannels * scaleWidth, histHeight),
			                    Cv.Point (h * imColorChannels * scaleWidth + 1, histHeight - _intensity),
				                    CvColor.Red, Cv.FILLED);

						// Draw Channel 2
						binVal = Cv.QueryHistValue_1D (hist2, h);
						_intensity = Cv.Round (binVal / maxValue * channelMax) * scaleHeight; // 0 to channelMax

						// Draw a rectangle (h, s) to (h+1, s+1)  (scaled by window size)
						//  The pixel value is the color of the histogram value at bin (h, s)
						hist_img.Rectangle (Cv.Point (h * imColorChannels * scaleWidth + 2, histHeight * scaleHeight),
			                    Cv.Point (h * imColorChannels * scaleWidth + 3, histHeight * scaleHeight - _intensity),
			                    CvColor.Blue, Cv.FILLED);

						// Draw Channel 3
						binVal = Cv.QueryHistValue_1D (hist3, h);
						_intensity = Cv.Round (binVal / maxValue * channelMax) * scaleHeight; // 0 to channelMax
			
						// Draw a rectangle (h, s) to (h+1, s+1)  (scaled by window size)
						//  The pixel value is the color of the histogram value at bin (h, s)
						hist_img.Rectangle (Cv.Point (h * imColorChannels * scaleWidth + 4, histHeight * scaleHeight),
			                    Cv.Point (h * imColorChannels * scaleWidth + 5, histHeight * scaleHeight - _intensity),
			                    CvColor.Green, Cv.FILLED);
			
				}
		
				Cv.ShowImage ("Histogram", hist_img);

				Cv.ReleaseImage (hist_img);
		
		}

	
		//  Takes an image and calculates its histogram in HSV color space
		// Color images have 3 channels (4 if you count alpha?)
		// Webcam captures them in (R)ed, (G)reen, (B)lue.
		// Convert to (H)ue, (S)aturation (V)alue to get better separation for thresholding
		CvHistogram CalculateHSVHistogram (IplImage _image)
		{

				// Hue, Saturation, Value or HSV is a color model that describes colors (hue or tint) 
				// in terms of their shade (saturation or amount of gray) 
				//	and their brightness (value or luminance).
				// For HSV, Hue range is [0,179], Saturation range is [0,255] and Value range is [0,255]

				// hue varies from 0 to 179, see cvtColor
				float hueMin = 0, hueMax = 179; 
				float[] hueRanges = new float[2]{ hueMin, hueMax };
				// saturation varies from 0 (black-gray-white) to
				// 255 (pure spectrum color)
				float satMin = 0, satMax = 255; 
				float[] saturationRanges = new float[2]{ satMin, satMax };

				float valMin = 0, valMax = 255; 
				float[] valueRanges = new float[2]{ valMin, valMax };

				float[][] ranges = {hueRanges, saturationRanges, valueRanges};
	
				// Note: You don't need to use all 3 channels for the histogram. 
				int hueBins = 32;  // Number of bins in the Hue histogram (more bins = narrower bins)
				int satBins = 32; // Number of bins in the Saturation histogram (more bins = narrower bins)
				int valueBins = 8;  // Number of bins in the Value histogram (more bins = narrower bins)

				float maxValue = 0, minValue = 0;  // Minimum and maximum value of calculated histogram

				// Number of bins per histogram channel
				// If we use all 3 channels (H, S, V) then the histogram will have 3 dimensions.
				int[] hist_size = new int[]{hueBins}; //, satBins}; //, valueBins};

				CvHistogram hist = new CvHistogram (hist_size, HistogramFormat.Array, ranges, true);

				using (IplImage _imageHSV = ConvertToHSV (_image)) // Convert the image to HSV
				// We could keep the image in R, G, B, A if we wanted to.
				// Just split the channels into R, G, B planes


				using (IplImage imgH = new IplImage (_imageHSV.Size, imBitDepth, 1))
				using (IplImage imgS = new IplImage (_imageHSV.Size, imBitDepth, 1))
				using (IplImage imgV = new IplImage (_imageHSV.Size, imBitDepth, 1)) {
			
						// Break image into H, S, V planes
						// If the image were RGB, then it would split into R, G, B planes respectively
						_imageHSV.CvtPixToPlane (imgH, imgS, imgV, null);  // Cv.Split also does this

						// Store HSV planes as an IplImage array to pass to openCV's hist function
						IplImage[] hsvPlanes = { imgH }; //, imgS}; //, imgV };

						hist.Calc (hsvPlanes, false, null);  // Call hist function (no accumulatation, no mask)

						// Do we need to normalize??
						hist.GetMinMaxValue (out minValue, out maxValue);
						// Scale the histogram to unity height
						hist.Normalize (_imageHSV.Width * _imageHSV.Height * hist.Dim * hueMax / maxValue);
		

				}

				return hist;  // Return the histogram

		}


		//  Takes an image and calculates its histogram for one channel.
		// Color images have 3 channels (4 if you count alpha?)
		// Webcam captures them in (R)ed, (G)reen, (B)lue.
		// Convert to (H)ue, (S)aturation (V)alue to get better separation for thresholding
		CvHistogram CalculateOneChannelHistogram (IplImage _image, int channelNum, float channelMax)
		{
		
				// Hue, Saturation, Value or HSV is a color model that describes colors (hue or tint) 
				// in terms of their shade (saturation or amount of gray) 
				//	and their brightness (value or luminance).
				// For HSV, Hue range is [0,179], Saturation range is [0,255] and Value range is [0,255]

				if (channelNum > imColorChannels) 
						Debug.LogError ("Desired channel number " + channelNum + " is out of range.");
	
				float channelMin = 0; 
				float[] channelRanges = new float[2]{ channelMin, channelMax };
				
				float[][] ranges = {channelRanges};
		
				// Note: You don't need to use all 3 channels for the histogram. 
				int channelBins = 80;  // Number of bins in the Hue histogram (more bins = narrower bins)
				
				// Number of bins per histogram channel
				// If we use all 3 channels (H, S, V) then the histogram will have 3 dimensions.
				int[] hist_size = new int[]{channelBins};
		
				CvHistogram hist = new CvHistogram (hist_size, HistogramFormat.Array, ranges, true);
		
				using (IplImage _imageHSV = ConvertToHSV (_image)) // Convert the image to HSV
			// We could keep the image in R, G, B, A if we wanted to.
			// Just split the channels into R, G, B planes
			
				using (IplImage imgChannel = new IplImage (_imageHSV.Size, imBitDepth, 1)) {
			
						// Break image into H, S, V planes
						// If the image were RGB, then it would split into R, G, B planes respectively

						switch (channelNum) {
						case 0:
								_imageHSV.CvtPixToPlane (imgChannel, null, null, null);  // Cv.Split also does this
								break;
						case 1:
								_imageHSV.CvtPixToPlane (null, imgChannel, null, null);  // Cv.Split also does this
								break;
						case 2:
								_imageHSV.CvtPixToPlane (null, null, imgChannel, null);  // Cv.Split also does this
								break;
						default:
								Debug.LogError ("Channel is out of range");
								_imageHSV.CvtPixToPlane (imgChannel, null, null, null);  // Cv.Split also does this
								break;
						}
						// Store HSV planes as an IplImage array to pass to openCV's hist function
						IplImage[] hsvPlanes = { imgChannel };
			
						hist.Calc (hsvPlanes, false, null);  // Call hist function (no accumulatation, no mask)
			
				}
		
				return hist;  // Return the histogram
		
		}

		// Call the Back Projection method and display the results in a window
		void DrawBackProjection (IplImage _image)
		{
				if (trackFlag) 
						Cv.ShowImage ("Back Projection", CalculateBackProjection (_image, _histogramToTrack));
		
		}

		IplImage CalculateBackProjection (IplImage _image, CvHistogram hist)
		{

				IplImage _backProject = new IplImage (_image.Size, imBitDepth, 1);

				using (IplImage _imageHSV = ConvertToHSV (_image)) // Convert the image to HSV
				using (IplImage imgH = new IplImage (_imageHSV.Size, imBitDepth, 1))
				using (IplImage imgS = new IplImage (_imageHSV.Size, imBitDepth, 1))
				using (IplImage imgV = new IplImage (_imageHSV.Size, imBitDepth, 1)) {
				
						// Break image into H, S, V planes
						// If the image were RGB, then it would split into R, G, B planes respectively
						_imageHSV.CvtPixToPlane (imgH, imgS, imgV, null);  // Cv.Split also does this

						// Store HSV planes as an IplImage array to pass to openCV's hist function
						IplImage[] hsvPlanes = { imgH}; //, imgS}; //, imgV};

						hist.CalcBackProject (hsvPlanes, _backProject);

				}
				return _backProject;

		}

		// Convert _outBox.BoxPoints (type CvPoint2D32f) into CvPoint[][] for use
		// in DrawPolyLine
		CvPoint[][] rectangleBoxPoint (CvPoint2D32f[] _box)
		{

				CvPoint[] pts = new CvPoint[_box.Length];
				for (int i = 0; i < _box.Length; i++)
						pts [i] = _box [i];  // Get the box coordinates (CvPoint)

				// Now we've got the 4 corners of the tracking box returned by CamShift
				// in a format that DrawPolyLine can use
				return (new CvPoint[][] {pts});


		}

		// Set up the initial values for the Kalman filter
		void InitializeKalmanFilter ()
		{

				// Create the Kalman Filter
				_kalman = Cv.CreateKalman (4, 2, 0);
		
				// Set the Kalman filter initial state
				_kalman.StatePre.Set2D (0, 0, _rectToTrack.X); // Initial X position
				_kalman.StatePre.Set2D (1, 0, _rectToTrack.Y); // Initial Y position
				_kalman.StatePre.Set2D (2, 0, 0);  // Initial X velocity
				_kalman.StatePre.Set2D (3, 0, 0);  // Initial Y velocity
			
				// Prediction Equations

				// X position is a function of previous X positions and previous X velocities
				_kalman.TransitionMatrix.Set2D (0, 0, 1);
				_kalman.TransitionMatrix.Set2D (1, 0, 0);
				_kalman.TransitionMatrix.Set2D (2, 0, 1);
				_kalman.TransitionMatrix.Set2D (3, 0, 0);

				// Y position is a function of previous Y positions and previous Y velocities
				_kalman.TransitionMatrix.Set2D (0, 1, 0);
				_kalman.TransitionMatrix.Set2D (1, 1, 1);
				_kalman.TransitionMatrix.Set2D (2, 1, 0);
				_kalman.TransitionMatrix.Set2D (3, 1, 1);

				// X velocity is a function of previous X velocities
				_kalman.TransitionMatrix.Set2D (0, 2, 0);
				_kalman.TransitionMatrix.Set2D (1, 2, 0);
				_kalman.TransitionMatrix.Set2D (2, 2, 1);
				_kalman.TransitionMatrix.Set2D (3, 2, 0);

				// Y velocity is a function of previous Y velocities
				_kalman.TransitionMatrix.Set2D (0, 3, 0);
				_kalman.TransitionMatrix.Set2D (1, 3, 0);
				_kalman.TransitionMatrix.Set2D (2, 3, 0);
				_kalman.TransitionMatrix.Set2D (3, 3, 1);

				// set Kalman Filter
				Cv.SetIdentity (_kalman.MeasurementMatrix, 1.0f);
				Cv.SetIdentity (_kalman.ProcessNoiseCov, 0.4f);  //0.4
				Cv.SetIdentity (_kalman.MeasurementNoiseCov, 3f); //3
				Cv.SetIdentity (_kalman.ErrorCovPost, 1.0f);

		}


		void DrawROIBox (IplImage _image)
		{

				_image.DrawRect (_rectToTrack, CvColor.Snow);

				Cv.ShowImage ("ROI", _image);

		}


		//  Use the CamShift algorithm to track to base histogram throughout the 
		// succeeding frames
		void CalculateCamShift (IplImage _image)
		{

				IplImage _backProject = CalculateBackProjection (_image, _histogramToTrack);
				
				// Create convolution kernel for erosion and dilation
				IplConvKernel elementErode = Cv.CreateStructuringElementEx (10, 10, 5, 5, ElementShape.Rect, null);
				IplConvKernel elementDilate = Cv.CreateStructuringElementEx (4, 4, 2, 2, ElementShape.Rect, null);

				// Try eroding and then dilating the back projection
				// Hopefully this will get rid of the noise in favor of the blob objects.
				Cv.Erode (_backProject, _backProject, elementErode, 1);
				Cv.Dilate (_backProject, _backProject, elementDilate, 1);
				

				if (backprojWindowFlag) {
						Cv.ShowImage ("Back Projection", _backProject);
				} 

				// Parameters returned by Camshift algorithm
				CvBox2D _outBox;
				CvConnectedComp _connectComp;

				// Set the criteria for the CamShift algorithm
				// Maximum 10 iterations and at least 1 pixel change in centroid
				CvTermCriteria term_criteria = Cv.TermCriteria (CriteriaType.Iteration | CriteriaType.Epsilon, 10, 1);

				// Draw object center based on Kalman filter prediction
				CvMat _kalmanPrediction = _kalman.Predict ();
		
				int predictX = Mathf.FloorToInt ((float)_kalmanPrediction.GetReal2D (0, 0));
				int predictY = Mathf.FloorToInt ((float)_kalmanPrediction.GetReal2D (1, 0));

				// Run the CamShift algorithm
				if (Cv.CamShift (_backProject, _rectToTrack, term_criteria, out _connectComp, out _outBox) > 0) {
		
						// Use the CamShift estimate of the object center to update the Kalman model
						CvMat _kalmanMeasurement = Cv.CreateMat (2, 1, MatrixType.F32C1);
						// Update Kalman model with raw data from Camshift estimate
						_kalmanMeasurement.Set2D (0, 0, _outBox.Center.X); // Raw X position
						_kalmanMeasurement.Set2D (1, 0, _outBox.Center.Y); // Raw Y position
						//_kalmanMeasurement.Set2D (2, 0, _outBox.Center.X - lastPosition.X);
						//_kalmanMeasurement.Set2D (3, 0, _outBox.Center.Y - lastPosition.Y);

						lastPosition.X = Mathf.FloorToInt (_outBox.Center.X);
						lastPosition.Y = Mathf.FloorToInt (_outBox.Center.Y);

						_kalman.Correct (_kalmanMeasurement); // Correct Kalman model with raw data

						// CamShift function returns two values: _connectComp and _outBox.

						//	_connectComp contains is the newly estimated position and size
						//  of the region of interest. This is passed into the subsequent
						// call to CamShift 
						// Update the ROI rectangle with CamShift's new estimate of the ROI
						_rectToTrack = CheckROIBounds (_connectComp.Rect);
						
						// Draw a rectangle over the tracked ROI
						// This method will draw the rectangle but won't rotate it.
						_image.DrawRect (_rectToTrack, CvColor.Aqua);
						_image.DrawMarker (predictX, predictY, CvColor.Aqua);

						// _outBox contains a rotated rectangle esimating the position, size, and orientation
						// of the object we want to track (specified by the initial region of interest).
						// We then take this estimation and draw a rotated bounding box.
						// This method will draw the rotated rectangle
						rotatedBoxToTrack = _outBox;

						// Draw a rotated rectangle representing Camshift's estimate of the 
						// object's position, size, and orientation.
						_image.DrawPolyLine (rectangleBoxPoint (_outBox.BoxPoints ()), true, CvColor.Red);


				} else {
						
						//Debug.Log ("Object lost by Camshift tracker");
		
						_image.DrawMarker (predictX, predictY, CvColor.Purple, MarkerStyle.CircleLine);

						_rectToTrack = CheckROIBounds (new CvRect (predictX - Mathf.FloorToInt (_rectToTrack.Width / 2),
			                                           predictY - Mathf.FloorToInt (_rectToTrack.Height / 2), 
						                            _rectToTrack.Width, _rectToTrack.Height));
						_image.DrawRect (_rectToTrack, CvColor.Purple);
						

				}

				if (trackWindowFlag)
						Cv.ShowImage ("Image", _image);

				Cv.ReleaseImage (_backProject);


		}


		// Converts Unity's Rect type to CvRect
		// CVRect has type int and Rect has type float
		CvRect ConvertRect2CvRect (Rect _roi)
		{
				CvRect _cvroi = new CvRect (Mathf.FloorToInt (_roi.x), 
		                            Mathf.FloorToInt (_roi.y), 
		                            Mathf.FloorToInt (_roi.width), 
		                            Mathf.FloorToInt (_roi.height));

				return _cvroi;

		}

		// Determine if pixel box (ROI) is within the bounds of the image
		// Bounds are (0, 0, imWidth, imHeight)
		CvRect CheckROIBounds (CvRect _roi)
		{

				int _x = _roi.X, _y = _roi.Y, 
				_width = Mathf.Abs (_roi.Width), _height = Mathf.Abs (_roi.Height);


				
				if (_roi.X < 0) {
						_x = 0;
						Debug.LogWarning ("X is outside of image");
				}
				
				if (_roi.Y < 0) {
						_y = 0;
						Debug.LogWarning ("Y is outside of image");
				}

				if (_roi.Width < 2)
						_width = 2;
				if (_roi.Height < 2)
						_height = 2;

				if ((_x + _width) > imWidth) {
						_width = Mathf.Abs (imWidth - _x);
						Debug.LogWarning ("Width is outside of image");
				}
				
				if ((_y + _height) > imHeight) {
						_height = Mathf.Abs (imHeight - _y);
						Debug.LogWarning ("Height is outside of image");
				}
				//Debug.Log (new CvRect (_x, _y, _width, _height));

				return new CvRect (_x, _y, _width, _height);
		}


		// Return a region of interest (_rect_roi) from within the image _image
		//  This doesn't need to be its own function, but I had so much trouble
		//  finding a method that didn't crash the program that I separated it.
		IplImage GetROI (IplImage _image, CvRect rect_roi)
		{
				// Get the region of interest
				CvMat img_roi;  // Get the region of interest

				// Grab the region of interest using the mouse-drawn box
				_image.GetSubRect (out img_roi, rect_roi);

				//Debug.Log (img_roi.GetSize ());
				
				return (Cv.GetImage (img_roi));  // Convert CvMat to IplImage
		}

		// Convert the Texture2D type of Unity to OpenCV's IplImage
		// This uses Adcock's parallel C# code to parallelize the conversion and make it faster
		void Texture2DToIplImage ()
		{

				Color[] pixels = _webcamTexture.GetPixels ();

				// Parallel for loop
				Parallel.For (0, imHeight, i =>
				{
						for (var j = 0; j < imWidth; j++) {
						
								var pixel = pixels [j + i * imWidth];
								var col = new CvScalar
				{
					Val0 = (double)pixel.b * 255,
					Val1 = (double)pixel.g * 255,
					Val2 = (double)pixel.r * 255
				};

								videoSourceImage.Set2D (i, j, col);
						}
				});

				
				// Non-parallelized code
//				for (var i = 0; i < imHeight; i++) {
//						for (var j = 0; j < imWidth; j++) {
//								var pixel = pixels [j + i * imWidth];
//								var col = new CvScalar
//								{
//									Val0 = (double)pixel.b * 255,
//									Val1 = (double)pixel.g * 255,
//									Val2 = (double)pixel.r * 255
//								};
//				
//								webcamera.Set2D (i, j, col);
//						}
//
//				}

				// Flip up/down dimension and right/left dimension
				if (!FlipUpDownAxis && FlipLeftRightAxis)
						Cv.Flip (videoSourceImage, videoSourceImage, FlipMode.XY);
				else if (!FlipUpDownAxis)
						Cv.Flip (videoSourceImage, videoSourceImage, FlipMode.X);
				else if (FlipLeftRightAxis)
						Cv.Flip (videoSourceImage, videoSourceImage, FlipMode.Y);
				         
		}


		// Draw Thesholded Image
		void DrawThresholdImage (IplImage _img)
		{


				// The HSV (hue, saturation, and value) range for thresholding
				// Hue, Saturation, Value or HSV is a color model that describes colors (hue or tint) 
				// in terms of their shade (saturation or amount of gray) 
				//	and their brightness (value or luminance).
				// In openCV, the ranges for HSV are: 
				// Hue range is [0,179], Saturation range is [0,255] and Value range is [0,255]
				// CvScalar(H, S, V)

				CvScalar _cvScalarFrom = new CvScalar (_hueLow, _satLow, 0), _cvScalarTo = new CvScalar (_hueHigh, _satHigh, 255);
				Cv.ShowImage ("HSV Thresholded Image", GetThresholdedImage (_img, _cvScalarFrom, _cvScalarTo));

		}

		// Threshold the image based on the HSV value
		//  From and To are CvScalars of 3 values {Hue, Saturation, and (Brightness) Value)
		IplImage GetThresholdedImage (IplImage img, CvScalar from, CvScalar to)
		{

				// Hue, Saturation, Value or HSV is a color model that describes colors (hue or tint) 
				// in terms of their shade (saturation or amount of gray) 
				//	and their brightness (value or luminance).
		
				// Hue is expressed as a number from 0 to 360 degrees representing hues of red (starts at 0), 
				// yellow (starts at 60), green (starts at 120), cyan (starts at 180), 
				// blue (starts at 240), and magenta (starts at 300).
				// Saturation is the amount of gray (0% to 100%) in the color.
				// Value (or Brightness) works in conjunction with saturation and 
				// describes the brightness or intensity of the color from 0% to 100%.

				IplImage imgHsv = ConvertToHSV (img);
				IplImage imgThreshed = Cv.CreateImage (Cv.GetSize (imgHsv), imBitDepth, 1);
				Cv.InRangeS (imgHsv, from, to, imgThreshed);
				Cv.ReleaseImage (imgHsv);
				return imgThreshed;
		}

		// Convert the image to HSV values
		IplImage ConvertToHSV (IplImage img)
		{
				IplImage imgHsv = Cv.CreateImage (Cv.GetSize (img), imBitDepth, imColorChannels);
				Cv.CvtColor (img, imgHsv, ColorConversion.BgrToHsv);

				return imgHsv;
		}

		// Use the two corners of the mouse-drawn box to define the pixel box (aka ROI)
		// Need to check to see if the box is actually on the texture
		// If not, then retrict pixel box dimensions to (0, 0, imHeight, imWidth).
		Rect MakePixelBox (Vector2 point1, Vector2 point2)
		{

				Vector2 _topLeft, _bottomRight;
				float boxHeight, boxWidth;

				boxHeight = Mathf.Abs (point1.y - point2.y) / objectScreenPosition.height * imHeight;
				boxWidth = Mathf.Abs (point1.x - point2.x) / objectScreenPosition.width * imWidth;

				// Top-Left corner
				// (0,0) is the bottom left corner
				// OpenCV puts (0,0) in top left corner
				_topLeft.x = (Mathf.Min (point1.x, point2.x) - objectScreenPosition.x) / objectScreenPosition.width * imWidth;  // Axis increases left to right
				_topLeft.y = ((Screen.height - Mathf.Max (point1.y, point2.y)) - objectScreenPosition.y)
						/ objectScreenPosition.height * imHeight; // Axis increases bottom to top

				// Clamp top left corner to within image limits
				_topLeft.x = Mathf.Clamp (_topLeft.x, 0, imWidth);
				_topLeft.y = Mathf.Clamp (_topLeft.y, 0, imHeight);

				// Clamp opposite corner within image limits
				_bottomRight.x = _topLeft.x + boxWidth;
				_bottomRight.y = _topLeft.y + boxHeight;
				_bottomRight.x = Mathf.Clamp (_bottomRight.x, 0, imWidth);
				_bottomRight.y = Mathf.Clamp (_bottomRight.y, 0, imHeight);

				// Recalculate height and width based on clamped values of corners
				boxHeight = Mathf.Abs (_bottomRight.y - _topLeft.y);
				boxWidth = Mathf.Abs (_bottomRight.x - _topLeft.x);

				return new Rect (_topLeft.x, _topLeft.y, boxWidth, boxHeight); 

		}
	

		// The gameObject might not have the same resolution as the webcam image
		// used by OpenCV. So we need to scale the position to the gameObject's resolution
		float scaleObjectHeight (float _height)
		{

				_height = _height / imHeight * objectScreenPosition.height; 

				return _height;

		}

		// The gameObject might not have the same resolution as the webcam image
		// used by OpenCV. So we need to scale the position to the gameObject's resolution
		float scaleObjectWidth (float _width)
		{
		
				_width = _width / imWidth * objectScreenPosition.width;
		
				return _width;
		
		}

		void OnGUI ()
		{

				// Display instructions when not tracking
				if (!trackFlag) {
						GUI.Box (new Rect (Screen.width - 600, Screen.height - 30, 500, 30), 
			         "Hold the mouse button down and drag to select a rectangular region to track");
				} else {
						GUI.Box (new Rect (0, 0, 230, 100), 
			         "Keyboard shortcuts\n" +
								"======================\n\n" +
								"h = Show histogram\n" +
								"b = Show back projection\n" +
								"t = Show Camshift tracking window");

				}

				// Draw the selection box to identify region to track 
				if (_mouseIsDown) {


						_mouseLastPos = Input.mousePosition;

						// Find the corner of the box
						Vector2 origin;

						origin.x = Mathf.Min (_mouseDownPos.x, _mouseLastPos.x);
						// GUI and mouse coordinates are the opposite way around.
						origin.y = Mathf.Max (_mouseDownPos.y, _mouseLastPos.y);

						//Compute size of box
						Vector2 size = _mouseDownPos - _mouseLastPos;       
								
						Rect rectBox = new Rect (origin.x, Screen.height - origin.y, Mathf.Abs (size.x), Mathf.Abs (size.y));
				
						GUI.Box (rectBox, "Region\nto\nTrack");  // Draw empty box as GUI overlay

						
				}
				// If tracking with CamShift, then draw GUI box over the tracked object
				else if (trackFlag) {

						
						// Figure out where the tracking box is relative to top-left corner of gameObject

						CvPoint p1 = rotatedBoxToTrack.BoxPoints () [1];  // Top left corner
						CvRect _rect = ConvertRect2CvRect (new Rect (p1.X, p1.Y, rotatedBoxToTrack.Size.Width, rotatedBoxToTrack.Size.Height));

						Vector2 origin;
						origin.x = objectScreenPosition.position.x + scaleObjectWidth (_rect.X);
						origin.y = objectScreenPosition.position.y + scaleObjectHeight (_rect.Y);

						
						GUIUtility.RotateAroundPivot (rotatedBoxToTrack.Angle, origin);
						GUI.Box (new Rect (origin.x, origin.y, scaleObjectWidth (_rect.Width),
							          scaleObjectHeight (_rect.Height)), "");
						
						// Rotate GUI opposite way so that successive GUI calls won't be rotated.
						GUIUtility.RotateAroundPivot (-rotatedBoxToTrack.Angle, origin);
				}
				
		}


		public void OnDestroy ()
		{
				Cv.ReleaseImage (videoSourceImage);
				Cv.DestroyAllWindows ();  // Does this Dispose too??

		}

		// Trackbar callback procedures
		void onTrackbarHueLow (int position)
		{
				if (position < _hueHigh)
						_hueLow = position;
				else {
						Cv.SetTrackbarPos ("Hue Low", "HSV image", _hueHigh);
						_hueLow = _hueHigh;
				}
		}
	
		void onTrackbarHueHigh (int position)
		{
				if (position > _hueLow)
						_hueHigh = position;
				else {
						Cv.SetTrackbarPos ("Hue High", "HSV image", _hueLow);
						_hueHigh = _hueLow;
				}
		}
	
		void onTrackbarSatLow (int position)
		{
				if (position < _satHigh)
						_satLow = position;
				else {
						Cv.SetTrackbarPos ("Sat Low", "HSV image", _satHigh);
						_satLow = _satHigh;
				}
		}
	
		void onTrackbarSatHigh (int position)
		{
				if (position > _satLow)
						_satHigh = position;
				else {
						Cv.SetTrackbarPos ("Sat High", "HSV image", _satLow);
						_satHigh = _satLow;
				}
		}


	
}